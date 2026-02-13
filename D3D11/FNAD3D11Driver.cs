using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShaderExtends.Base;
using ShaderExtends.Core;
using ShaderExtends.Interfaces;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terraria;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Blend = Vortice.Direct3D11.Blend;
using TextureAddressMode = Vortice.Direct3D11.TextureAddressMode;
using Viewport = Vortice.Mathematics.Viewport;
using XnaBlend = Microsoft.Xna.Framework.Graphics.Blend;
using XnaBlendFunction = Microsoft.Xna.Framework.Graphics.BlendFunction;
using XnaCompareFunction = Microsoft.Xna.Framework.Graphics.CompareFunction;
using XnaCullMode = Microsoft.Xna.Framework.Graphics.CullMode;
using XnaFillMode = Microsoft.Xna.Framework.Graphics.FillMode;
using XnaStencilOperation = Microsoft.Xna.Framework.Graphics.StencilOperation;

namespace ShaderExtends.D3D11
{
    public unsafe class FNAD3D11Driver : IDisposable, IFNARenderDriver
    {

        private float _depthEpsilon = 0f;
        private const float EpsilonStep = 0.000001f;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ID3D11Device _d3dDevice;
        private readonly ID3D11DeviceContext _d3dContext;
        private readonly ID3D11SamplerState _samplerState;
        private readonly ID3D11BlendState _blendState;

        private bool _isActive = false;
        private IFCSMaterial _currentMaterial;
        private RenderTarget2D _currentDestination;
        private SpriteSortMode _currentSortMode;
        private List<DrawQueueItem> _drawQueue = new();
        public IFCSMaterial CurrentMaterial
        {
            get { return _currentMaterial; }
            set { _currentMaterial = value; }
        }

        private struct DrawQueueItem
        {
            public Texture2D Source;
            public IntPtr VBufPtr;
            public int Stride;
            public int Count;
            public IntPtr TextureHandle;
        }

        private ID3D11Buffer _dynamicVertexBuffer; // 全局复用的动态顶点缓冲区
        private int _dynamicVertexBufferSize = 0;

        // 原始绘图指令（对应 RawPool）
        private struct DrawCommand
        {
            public IntPtr TextureHandle;
            public Texture2D SourceTexture; //以此获取宽高
            public int RawOffset;    // 在 RawPool 中的字节偏移
            public int VertexCount;
            public float SortDepth;  // 用于 BackToFront 排序
            public bool IsVertexless; // 无顶点数据（仅依赖 SV_VertexID）
        }

        // 合并后的 GPU 指令（对应 SortedPool）
        private struct GpuBatchCommand
        {
            public IntPtr TextureHandle;
            public Texture2D SourceTexture;
            public int StartVertex;  // 在 GPU Buffer 中的起始顶点下标
            public int VertexCount;
            public bool IsVertexless; // 无顶点数据批次
        }

        private List<DrawCommand> _drawCommands = new List<DrawCommand>(2048);
        private List<GpuBatchCommand> _gpuBatches = new List<GpuBatchCommand>(128);

        DeviceState state;

        public bool IsActive => _isActive;
        public SpriteSortMode CurrentSortMode => _currentSortMode;

        private BlendState _currentBlendState;
        private RasterizerState _currentRasterizerState;
        private DepthStencilState _currentDepthStencilState;

        public FNAD3D11Driver(GraphicsDevice device, FNA3D_SysRendererEXT backend)
        {
            _graphicsDevice = device;
            _d3dDevice = MarshallingHelpers.FromPointer<ID3D11Device>(backend.d3d11_device);
            _d3dContext = MarshallingHelpers.FromPointer<ID3D11DeviceContext>(backend.d3d11_context);

            _samplerState = _d3dDevice.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });

            var blendDesc = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };
            blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = false,
                SourceBlend = Blend.One,
                DestinationBlend = Blend.Zero,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.Zero,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };

            _blendState = _d3dDevice.CreateBlendState(blendDesc);
        }

        public void Begin(
            RenderTarget2D destination = null,
            SpriteSortMode sortMode = SpriteSortMode.Deferred,
            BlendState blendState = null,
            SamplerState samplerState = null,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            Microsoft.Xna.Framework.Matrix? transformMatrix = null,
            IFCSMaterial material = null)
        {
            if (_isActive)
                throw new InvalidOperationException("批处理已在进行中，请先调用 End()");
            SaveDeviceState(out state);
            _isActive = true;
            _currentMaterial = material;
            _currentDestination = destination;
            _currentSortMode = sortMode;
            _currentBlendState = blendState;
            _currentRasterizerState = rasterizerState;
            _currentDepthStencilState = depthStencilState;
            _drawQueue.Clear();
            _drawCommands.Clear();
            _gpuBatches.Clear();
        }


        public void Draw(Texture2D source, IntPtr vBufPtr, int stride, int count)
            => Draw(source, vBufPtr, stride, count, 0f);

        public void Draw(Texture2D source, IntPtr vBufPtr, int stride, int count, float depth)
        {
            if (!_isActive)
                throw new InvalidOperationException("批处理未启动，请先调用 Begin()");

            if (_currentSortMode == SpriteSortMode.Immediate)
            {
                ProcessDrawImmediate(source, vBufPtr, stride, count);
            }
            else
            {
                bool vertexless = vBufPtr == IntPtr.Zero;
                long offsetLong = 0;

                if (!vertexless)
                {
                    // 计算在 RawPool 中的相对偏移量 (字节单位)
                    // 假设 vBufPtr 是从 GlobalVertexPool.RawPool 分配的
                    offsetLong = (long)vBufPtr - (long)GlobalVertexPool.RawPool.GetBasePtr();
                }

                // 直接存入 _drawCommands，供 ProcessDrawQueue 使用
                _drawCommands.Add(new DrawCommand
                {
                    TextureHandle = D3D11TextureHelper.GetD3D11SRV_Method2(source),
                    SourceTexture = source,
                    RawOffset = (int)offsetLong,
                    VertexCount = count,
                    SortDepth = depth,
                    IsVertexless = vertexless
                });
            }
        }
        /// <summary>
        /// Immediate 模式下的立即执行（假设着色器已由外部通过 Material.Apply 设置）
        /// </summary>
        private unsafe void ProcessDrawImmediate(Texture2D source, IntPtr vBufPtr, int stride, int count)
        {
            // 1. 获取主纹理的原始句柄
            IntPtr finalSRVHandle = D3D11TextureHelper.GetD3D11SRV_Method2(source);
            var material = _currentMaterial as D3D11FCSMaterial;
            if (material == null) return;

            // --- A. Compute Shader (CS) 阶段 ---
            if (material.Effect.CS != null)
            {
                material.SyncToDevice();
                material.EnsureShadow(source.Width, source.Height);
                var shadow = material.Shadow as D3D11ShadowBuffer;
                if (shadow != null)
                {
                    _d3dContext.CSSetShader(material.Effect.CS);

                    // 【多纹理支持 - CS输入】
                    // 绑定主纹理到 CS t0
                    var mainSRVForCS = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(finalSRVHandle);
                    _d3dContext.CSSetShaderResource(0, mainSRVForCS);

                    // 绑定材质中的额外纹理到 CS (t1, t2...)
                    if (material.SourceTexture != null)
                    {   
                        for (int i = 1; i < material.SourceTexture.Length; i++)
                        {
                            if (material.SourceTexture[i] == null) continue;
                            var extraSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(
                                D3D11TextureHelper.GetD3D11SRV_Method2(material.SourceTexture[i] as Texture2D));
                            _d3dContext.CSSetShaderResource((uint)i, extraSRV);
                        }
                    }

                    _d3dContext.CSSetUnorderedAccessView(0, shadow.UAV);
                    _d3dContext.Dispatch((uint)material.GroupsX, (uint)material.GroupsY, (uint)material.GroupsZ);

                    // 清理 CS 绑定，防止资源冲突 (Hazard)
                    _d3dContext.CSSetUnorderedAccessView(0, null);
                    for (int slot = 0; slot < 8; slot++) _d3dContext.CSSetShaderResource((uint)slot, null);

                    if (material.Effect.VS == null || material.Effect.PS == null)
                    {
                        ID3D11RenderTargetView[] currentRTVs = new ID3D11RenderTargetView[1];
                        _d3dContext.OMGetRenderTargets(1, currentRTVs, out _);
                        if (currentRTVs[0] != null)
                        {
                            var destResource = currentRTVs[0].Resource;
                            _d3dContext.CopyResource(destResource, shadow.Texture);
                            destResource.Dispose();
                            currentRTVs[0].Dispose();
                        }
                        return;
                    }
                    finalSRVHandle = shadow.SRVHandle;
                }
            }

            // --- B. 准备像素着色器 (PS) 环境 ---
            material.SyncToDevice();
            ApplyGraphicsState();

            _d3dContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _d3dContext.VSSetShader(material.Effect.VS);
            _d3dContext.PSSetShader(material.Effect.PS);

            // 1. 动态绑定常量缓冲区 (CBuffer)
            foreach (var cbMeta in material.Effect.Metadata.Buffers)
            {
                var buf = material.GetBuffer(cbMeta.Slot);
                if (buf != null)
                {
                    _d3dContext.VSSetConstantBuffer((uint)cbMeta.Slot, buf);
                    _d3dContext.PSSetConstantBuffer((uint)cbMeta.Slot, buf);
                }
            }

            // 2. 设置采样器 (通常 s0 为通用采样器)
            _d3dContext.PSSetSampler(0, _samplerState);

            // 3. 【核心修改：多纹理绑定 - PS输入】
            // 绑定主纹理到 PS t0
            var finalMainSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(finalSRVHandle);
            _d3dContext.PSSetShaderResource(0, finalMainSRV);

            // 绑定材质中的所有额外纹理 (t1, t2, t3...)
            if (material.SourceTexture != null)
            {
                for (int i = 1; i < material.SourceTexture.Length; i++)
                {
                    if (material.SourceTexture[i] == null) continue;
                    var extraSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(
                        D3D11TextureHelper.GetD3D11SRV_Method2(material.SourceTexture[i] as Texture2D));
                    _d3dContext.PSSetShaderResource((uint)i, extraSRV);
                }
            }

            // 4. 设置视口
            _d3dContext.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, source.Width, source.Height));

            // --- C. 执行 Draw ---
            if (vBufPtr == IntPtr.Zero)
            {
                _d3dContext.IASetInputLayout(null);
                _d3dContext.IASetVertexBuffers(0, 1, new ID3D11Buffer[] { null }, new uint[] { 0 }, new uint[] { 0 });
                _d3dContext.Draw((uint)count, 0);
            }
            else
            {
                _d3dContext.IASetInputLayout(material.Effect.Layout);
                var vBuf = MarshallingHelpers.FromPointer<ID3D11Buffer>(vBufPtr);
                _d3dContext.IASetVertexBuffer(0, vBuf, (uint)stride, 0);
                _d3dContext.Draw((uint)count, 0);
            }
        }


        private void ProcessDrawQueue()
        {
            if (_drawCommands.Count == 0) return;

            ID3D11RenderTargetView[] rtvs = new ID3D11RenderTargetView[1];
            _d3dContext.OMGetRenderTargets(1, rtvs, out _);

            uint targetWidth = (uint)Main.screenWidth;  // 默认回退值
            uint targetHeight = (uint)Main.screenHeight;

            if (rtvs[0] != null)
            {
                // 2. 从 RTV 反推 Texture2D 资源
                using (var resource = rtvs[0].Resource)
                using (var tex2D = resource.QueryInterface<ID3D11Texture2D>())
                {
                    // 3. 获取真正的尺寸！
                    targetWidth = tex2D.Description.Width;
                    targetHeight = tex2D.Description.Height;
                }
                // 注意：Vortice/SharpDX 的 COM 对象需要释放，rtvs[0] 也要释放
                rtvs[0].Dispose();
            }


            _gpuBatches.Clear();

            var material = _currentMaterial as D3D11FCSMaterial;
            int stride = material.VertexStride;

            // ---------------------------------------------------------
            // 1. 排序 (Sorting) - 决定绘制顺序
            // ---------------------------------------------------------
            switch (_currentSortMode)
            {
                case SpriteSortMode.Texture:
                    // 纯粹按纹理排序，追求最少 DrawCall
                    _drawCommands.Sort((a, b) => a.TextureHandle.CompareTo(b.TextureHandle));
                    break;

                case SpriteSortMode.BackToFront:
                    // 深度优先：从远到近绘制
                    // 如果深度相同，则按纹理排序以尝试合批
                    _drawCommands.Sort((a, b) =>
                    {
                        int depthCompare = b.SortDepth.CompareTo(a.SortDepth); // 降序
                        return depthCompare != 0 ? depthCompare : a.TextureHandle.CompareTo(b.TextureHandle);
                    });
                    break;

                case SpriteSortMode.FrontToBack:
                    // 深度优先：从近到远绘制
                    _drawCommands.Sort((a, b) =>
                    {
                        int depthCompare = a.SortDepth.CompareTo(b.SortDepth); // 升序
                        return depthCompare != 0 ? depthCompare : a.TextureHandle.CompareTo(b.TextureHandle);
                    });
                    break;

                    // Deferred 模式保持提交顺序，不排序
            }

            // ---------------------------------------------------------
            // 2. 紧凑化 & 动态合批 (Compaction & Batching)
            // ---------------------------------------------------------
            byte* rawBase = (byte*)GlobalVertexPool.RawPool.GetBasePtr();
            byte* sortedBase = (byte*)GlobalVertexPool.SortedPool.GetBasePtr();

            int currentSortedOffset = 0; // 目标池的字节偏移
            int totalVertexCount = 0;    // 总顶点数

            // 读取第一个指令初始化 Batch 状态
            var firstCmd = _drawCommands[0];
            IntPtr currentBatchTex = firstCmd.TextureHandle;
            Texture2D currentBatchSource = firstCmd.SourceTexture;
            bool currentBatchIsVertexless = firstCmd.IsVertexless;
            int batchStartVertex = 0;
            int batchVertexCount = 0;

            int commandCount = _drawCommands.Count;
            for (int i = 0; i < commandCount; i++)
            {
                var cmd = _drawCommands[i];
                int bytesToCopy = cmd.IsVertexless ? 0 : cmd.VertexCount * stride;

                // A. 内存拷贝：从 RawPool 搬运到 SortedPool 的连续位置
                if (bytesToCopy > 0)
                {
                    int cap = GlobalVertexPool.SortedPool.GetCapacity();
                    int required = currentSortedOffset + bytesToCopy;
                    if (required > cap)
                        throw new InvalidOperationException($"SortedPool overflow: need {required} bytes, capacity {cap}");

                    Buffer.MemoryCopy(
                        rawBase + cmd.RawOffset,           // 源地址
                        sortedBase + currentSortedOffset,  // 目标地址
                        cap - currentSortedOffset,
                        bytesToCopy
                    );
                }

                // B. 合批判定
                bool sameTexture = (cmd.TextureHandle == currentBatchTex);
                bool sameVertexKind = (cmd.IsVertexless == currentBatchIsVertexless);
                bool needSplit = !(sameTexture && sameVertexKind);

                if (needSplit)
                {
                    _gpuBatches.Add(new GpuBatchCommand
                    {
                        TextureHandle = currentBatchTex,
                        SourceTexture = currentBatchSource,
                        StartVertex = batchStartVertex,
                        VertexCount = batchVertexCount,
                        IsVertexless = currentBatchIsVertexless
                    });

                    currentBatchTex = cmd.TextureHandle;
                    currentBatchSource = cmd.SourceTexture;
                    currentBatchIsVertexless = cmd.IsVertexless;
                    batchStartVertex = totalVertexCount; // 对于 Vertexless 无意义，但保持占位
                    batchVertexCount = 0;
                }

                currentSortedOffset += bytesToCopy;
                if (!cmd.IsVertexless)
                {
                    totalVertexCount += cmd.VertexCount;
                }
                batchVertexCount += cmd.VertexCount;
            }

            // 循环结束后，不要忘记添加最后一个 Batch
            _gpuBatches.Add(new GpuBatchCommand
            {
                TextureHandle = currentBatchTex,
                SourceTexture = currentBatchSource,
                StartVertex = batchStartVertex,
                VertexCount = batchVertexCount,
                IsVertexless = currentBatchIsVertexless
            });

            // ---------------------------------------------------------
            // 3. 上传到 GPU (Upload)
            // ---------------------------------------------------------
            int totalBytesNeeded = totalVertexCount * stride;
            if (totalBytesNeeded > 0)
            {
                EnsureDynamicBufferSize(totalBytesNeeded);

                // Map -> Copy -> Unmap (高性能动态更新)
                var mapBox = _d3dContext.Map(_dynamicVertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                Buffer.MemoryCopy(sortedBase, (void*)mapBox.DataPointer, mapBox.RowPitch, totalBytesNeeded);
                _d3dContext.Unmap(_dynamicVertexBuffer, 0);
            }
            else
            {
                // 本批次完全无顶点数据，确保不会误用旧的 VB
                _dynamicVertexBuffer?.Dispose();
                _dynamicVertexBuffer = null;
                _dynamicVertexBufferSize = 0;
            }

            // ---------------------------------------------------------
            // 4. 执行绘制 (Execute DrawCalls)
            // ---------------------------------------------------------
            try
            {
                material.SyncToDevice();
                ApplyGraphicsState();

                // 设置全局状态
                _d3dContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _d3dContext.VSSetShader(material.Effect.VS);
                _d3dContext.PSSetShader(material.Effect.PS);
                _d3dContext.IASetInputLayout(material.Effect.Layout);


                // 绑定包含了所有排序后顶点的大 Buffer
                _d3dContext.IASetVertexBuffer(0, _dynamicVertexBuffer, (uint)stride, 0);

                // 常量缓冲
                for (int i = 0; i < 8; i++)
                {
                    var buf = material.GetBuffer(i);
                    if (buf != null)
                    {
                        _d3dContext.VSSetConstantBuffer((uint)i, buf);
                        _d3dContext.PSSetConstantBuffer((uint)i, buf);
                    }
                }
                _d3dContext.PSSetSampler(0, _samplerState);

                // 绑定材质中的额外纹理 (t1, t2, t3...)
                if (material.SourceTexture != null)
                {
                    for (int s = 1; s < material.SourceTexture.Length; s++)
                    {
                        if (material.SourceTexture[s] == null) continue;
                        var extraSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(
                            D3D11TextureHelper.GetD3D11SRV_Method2(material.SourceTexture[s] as Texture2D));
                        _d3dContext.PSSetShaderResource((uint)s, extraSRV);
                    }
                }

                // 遍历批次进行绘制
                IntPtr lastBoundTex = IntPtr.Zero;
                int batchCount = _gpuBatches.Count; // 缓存 Count 略微提升性能
                ID3D11InputLayout lastLayout = material.Effect.Layout;

                for (int i = 0; i < batchCount; i++)
                {
                    var batch = _gpuBatches[i];
                    IntPtr finalSRVHandle = batch.TextureHandle;
                    // --- 新增：D3D11 Compute Shader 处理逻辑 ---
                    if (material.Effect.CS != null)
                    {
                        // 1. 同步材质参数（如时间、分辨率等）
                        material.SyncToDevice();

                        // 2. 准备输出缓存（对应 OpenGL 的 ShadowBuffer/ImageTexture）
                        material.EnsureShadow(batch.SourceTexture.Width, batch.SourceTexture.Height);
                        var shadow = material.Shadow as D3D11ShadowBuffer; // 假设你有这个类

                        // 3. 绑定输入与输出
                        var inputSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(batch.TextureHandle);
                        _d3dContext.CSSetShader(material.Effect.CS);
                        _d3dContext.CSSetShaderResource(0, inputSRV);

                        // 绑定材质中的额外纹理到 CS (t1, t2...)
                        if (material.SourceTexture != null)
                        {
                            for (int s = 1; s < material.SourceTexture.Length; s++)
                            {
                                if (material.SourceTexture[s] == null) continue;
                                var csExtraSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(
                                    D3D11TextureHelper.GetD3D11SRV_Method2(material.SourceTexture[s] as Texture2D));
                                _d3dContext.CSSetShaderResource((uint)s, csExtraSRV);
                            }
                        }

                        _d3dContext.CSSetUnorderedAccessView(0, shadow.UAV); // CS 写入需要 UAV

                        // 4. 执行调度
                        _d3dContext.Dispatch((uint)material.GroupsX, (uint)material.GroupsY, (uint)material.GroupsZ);

                        // 5. 解绑 UAV，以便后续作为 SRV 给 PS 使用
                        _d3dContext.CSSetUnorderedAccessView(0, null);
                        _d3dContext.CSSetShaderResource(0, null); // 建议加上这行

                        if (material.Effect.VS == null || material.Effect.PS == null)
                        {
                            var targets = Main.spriteBatch.GraphicsDevice.GetRenderTargets();
                            var destResource = MarshallingHelpers.FromPointer<ID3D11Resource>(D3D11TextureHelper.GetD3D11ResourceHandle(targets[0].RenderTarget as RenderTarget2D));
                            _d3dContext.CopyResource(destResource, shadow.Texture);
                            return;
                        }

                        // 将绘制用的纹理切换为 CS 处理后的结果
                        finalSRVHandle = shadow.SRVHandle;
                    }

                    _d3dContext.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, targetWidth, targetHeight));
                    // 仅在纹理变化时切换 SRV
                    if (finalSRVHandle != lastBoundTex)
                    {
                        var srv = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(finalSRVHandle);
                        _d3dContext.PSSetShaderResource(0, srv);
                        lastBoundTex = finalSRVHandle;
                    }

                    // 无顶点数据的批次需要解绑输入布局和顶点缓冲
                    if (batch.IsVertexless)
                    {
                        if (lastLayout != null)
                        {
                            _d3dContext.IASetInputLayout(null);
                            lastLayout = null;
                        }

                        _d3dContext.IASetVertexBuffers(0, 1, new ID3D11Buffer[] { null }, new uint[] { 0 }, new uint[] { 0 });
                        _d3dContext.Draw((uint)batch.VertexCount, 0);
                    }
                    else
                    {
                        if (lastLayout == null)
                        {
                            _d3dContext.IASetInputLayout(material.Effect.Layout);
                            lastLayout = material.Effect.Layout;
                        }

                        _d3dContext.IASetVertexBuffer(0, _dynamicVertexBuffer, (uint)stride, 0);
                        _d3dContext.Draw((uint)batch.VertexCount, (uint)batch.StartVertex);
                    }
                }
            }
            catch
            {

            }
        }

        private void EnsureDynamicBufferSize(int sizeInBytes)
        {
            if (_dynamicVertexBuffer == null || _dynamicVertexBufferSize < sizeInBytes)
            {
                _dynamicVertexBuffer?.Dispose();

                // 稍微多分配一点，避免频繁扩容
                _dynamicVertexBufferSize = Math.Max(sizeInBytes, _dynamicVertexBufferSize * 2);

                var desc = new BufferDescription
                {
                    ByteWidth = (uint)_dynamicVertexBufferSize,
                    Usage = ResourceUsage.Dynamic,          // 关键：动态
                    BindFlags = BindFlags.VertexBuffer,
                    CPUAccessFlags = CpuAccessFlags.Write,  // 关键：CPU可写
                    StructureByteStride = 0
                };
                _dynamicVertexBuffer = _d3dDevice.CreateBuffer(desc);
            }
        }

        /// <summary>
        /// 应用图形状态（混合、深度模板、光栅化）
        /// </summary>
        private void ApplyGraphicsState()
        {
            // 应用深度模板状态
            ID3D11DepthStencilState depthStencilState = null;
            if (_currentDepthStencilState != null)
            {
                depthStencilState = CreateD3D11DepthStencilState(_currentDepthStencilState);
            }
            _d3dContext.OMSetDepthStencilState(depthStencilState, (uint)(_currentDepthStencilState?.ReferenceStencil ?? 0));

            // 应用混合状态
            ID3D11BlendState blendState = _blendState;
            if (_currentBlendState != null)
            {
                blendState = CreateD3D11BlendState(_currentBlendState) ?? _blendState;
            }
            _d3dContext.OMSetBlendState(blendState, new Color4(0, 0, 0, 0), 0xffffffff);

            // 应用光栅化状态
            ID3D11RasterizerState rasterizerState = null;
            if (_currentRasterizerState != null)
            {
                rasterizerState = CreateD3D11RasterizerState(_currentRasterizerState);
            }
            _d3dContext.RSSetState(rasterizerState);
        }

        /// <summary>
        /// 从 FNA BlendState 提取 D3D11 BlendState
        /// </summary>
        private ID3D11BlendState ExtractD3D11BlendState(BlendState blendState)
        {
            if (blendState == null) return null;

            try
            {
                // 尝试通过反射获取内部的 D3D11BlendState
                var field = typeof(BlendState).GetField("glBlendState",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    var glState = field.GetValue(blendState);
                    if (glState is ID3D11BlendState d3dBlendState)
                    {
                        return d3dBlendState;
                    }
                }
            }
            catch
            {
                // 如果反射失败，返回 null 使用默认值
            }

            return null;
        }

        /// <summary>
        /// 从 FNA DepthStencilState 提取 D3D11 DepthStencilState
        /// </summary>
        private ID3D11DepthStencilState ExtractD3D11DepthStencilState(DepthStencilState depthStencilState)
        {
            if (depthStencilState == null) return null;

            try
            {
                var field = typeof(DepthStencilState).GetField("glDepthStencilState",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    var glState = field.GetValue(depthStencilState);
                    if (glState is ID3D11DepthStencilState d3dDepthStencilState)
                    {
                        return d3dDepthStencilState;
                    }
                }
            }
            catch
            {
                // 如果反射失败，返回 null 使用默认值
            }

            return null;
        }

        /// <summary>
        /// 从 FNA RasterizerState 提取 D3D11 RasterizerState
        /// </summary>
        private ID3D11RasterizerState ExtractD3D11RasterizerState(RasterizerState rasterizerState)
        {
            if (rasterizerState == null) return null;

            try
            {
                var field = typeof(RasterizerState).GetField("glRasterizerState",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    var glState = field.GetValue(rasterizerState);
                    if (glState is ID3D11RasterizerState d3dRasterizerState)
                    {
                        return d3dRasterizerState;
                    }
                }
            }
            catch
            {
                // 如果反射失败，返回 null 使用默认值
            }

            return null;
        }

        private void SaveDeviceState(out DeviceState state)
        {
            state = new DeviceState();
            state.OldVS = _d3dContext.VSGetShader();
            state.OldPS = _d3dContext.PSGetShader();
            state.OldCS = _d3dContext.CSGetShader();
            state.OldLayout = _d3dContext.IAGetInputLayout();
            state.OldTopology = _d3dContext.IAGetPrimitiveTopology();

            _d3dContext.IAGetIndexBuffer(out state.OldIndexBuffer, out state.OldIndexFormat, out state.OldIndexOffset);

            state.OldVertexBuffers = new ID3D11Buffer[1];
            state.OldStrides = new uint[1];
            state.OldOffsets = new uint[1];
            _d3dContext.IAGetVertexBuffers(0, 1, state.OldVertexBuffers, state.OldStrides, state.OldOffsets);

            state.OldBlend = null;
            state.OldFactors = new float[4];
            fixed (float* pFactors = state.OldFactors)
                _d3dContext.OMGetBlendState(out state.OldBlend, pFactors, out state.OldSampleMask);

            _d3dContext.OMGetDepthStencilState(out state.OldDepth, out state.OldStencilRef);
            state.OldRaster = _d3dContext.RSGetState();

            state.OldVSBuffers = new ID3D11Buffer[8];
            state.OldPSBuffers = new ID3D11Buffer[8];
            _d3dContext.VSGetConstantBuffers(0, 8, state.OldVSBuffers);
            _d3dContext.PSGetConstantBuffers(0, 8, state.OldPSBuffers);

            state.OldSRVs = new ID3D11ShaderResourceView[1];
            _d3dContext.PSGetShaderResources(0, 1, state.OldSRVs);
            state.OldSamps = new ID3D11SamplerState[1];
            _d3dContext.PSGetSamplers(0, 1, state.OldSamps);

            state.OldViewports = new Vortice.Mathematics.Viewport[16];
            uint viewportCount = 16;
            _d3dContext.RSGetViewports(ref viewportCount, state.OldViewports);
            if (viewportCount < 16) Array.Resize(ref state.OldViewports, (int)viewportCount);
        }

        private void RestoreDeviceState(in DeviceState state)
        {
            _d3dContext.PSSetShaderResource(0, null);
            _d3dContext.IASetVertexBuffers(0, 1, new ID3D11Buffer[] { null }, new uint[] { 0 }, new uint[] { 0 });
            _d3dContext.VSSetConstantBuffers(0, 8, new ID3D11Buffer[8]);
            _d3dContext.PSSetConstantBuffers(0, 8, new ID3D11Buffer[8]);
            _d3dContext.Flush();

            _d3dContext.VSSetShader(state.OldVS);
            _d3dContext.PSSetShader(state.OldPS);
            _d3dContext.CSSetShader(state.OldCS);
            _d3dContext.IASetInputLayout(state.OldLayout);
            _d3dContext.IASetPrimitiveTopology(state.OldTopology);
            _d3dContext.IASetIndexBuffer(state.OldIndexBuffer, state.OldIndexFormat, state.OldIndexOffset);
            _d3dContext.IASetVertexBuffers(0, 1, state.OldVertexBuffers, state.OldStrides, state.OldOffsets);

            _d3dContext.VSSetConstantBuffers(0, 8, state.OldVSBuffers);
            _d3dContext.PSSetConstantBuffers(0, 8, state.OldPSBuffers);
            _d3dContext.PSSetShaderResource(0, state.OldSRVs[0]);
            _d3dContext.PSSetSampler(0, state.OldSamps[0]);

            fixed (float* pFactors = state.OldFactors)
                _d3dContext.OMSetBlendState(state.OldBlend, pFactors, state.OldSampleMask);

            _d3dContext.OMSetDepthStencilState(state.OldDepth, state.OldStencilRef);
            _d3dContext.RSSetState(state.OldRaster);
            _d3dContext.RSSetViewports(state.OldViewports);
        }

        private struct DeviceState
        {
            public ID3D11VertexShader OldVS;
            public ID3D11PixelShader OldPS;
            public ID3D11ComputeShader OldCS;
            public ID3D11InputLayout OldLayout;
            public PrimitiveTopology OldTopology;
            public ID3D11Buffer OldIndexBuffer;
            public Format OldIndexFormat;
            public uint OldIndexOffset;
            public ID3D11Buffer[] OldVertexBuffers;
            public uint[] OldStrides;
            public uint[] OldOffsets;
            public ID3D11BlendState OldBlend;
            public float[] OldFactors;
            public uint OldSampleMask;
            public ID3D11DepthStencilState OldDepth;
            public uint OldStencilRef;
            public ID3D11RasterizerState OldRaster;
            public ID3D11Buffer[] OldVSBuffers;
            public ID3D11Buffer[] OldPSBuffers;
            public ID3D11ShaderResourceView[] OldSRVs;
            public ID3D11SamplerState[] OldSamps;
            public Viewport[] OldViewports;
        }

        public void Dispose()
        {
            _samplerState?.Dispose();
            _blendState?.Dispose();
        }

        /// <summary>
        /// 从 FNA BlendState 创建 D3D11 BlendState
        /// </summary>
        private ID3D11BlendState CreateD3D11BlendState(BlendState blendState)
        {
            if (blendState == null) return null;

            // 根据 FNA BlendState 的属性构建 D3D11 混合描述
            var blendDesc = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };

            blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = blendState.BlendFactor != Microsoft.Xna.Framework.Color.White || blendState.AlphaBlendFunction != BlendFunction.Add,
                SourceBlend = ConvertBlend(blendState.ColorSourceBlend),
                DestinationBlend = ConvertBlend(blendState.ColorDestinationBlend),
                BlendOperation = ConvertBlendOperation(blendState.ColorBlendFunction),
                SourceBlendAlpha = ConvertBlend(blendState.AlphaSourceBlend),
                DestinationBlendAlpha = ConvertBlend(blendState.AlphaDestinationBlend),
                BlendOperationAlpha = ConvertBlendOperation(blendState.AlphaBlendFunction),
                RenderTargetWriteMask = ColorWriteEnable.All
            };

            return _d3dDevice.CreateBlendState(blendDesc);
        }

        /// <summary>
        /// 从 FNA DepthStencilState 创建 D3D11 DepthStencilState
        /// </summary>
        private ID3D11DepthStencilState CreateD3D11DepthStencilState(DepthStencilState depthStencilState)
        {
            if (depthStencilState == null) return null;

            var depthDesc = new Vortice.Direct3D11.DepthStencilDescription
            {
                DepthEnable = depthStencilState.DepthBufferEnable,
                DepthWriteMask = depthStencilState.DepthBufferWriteEnable ? DepthWriteMask.All : DepthWriteMask.Zero,
                DepthFunc = ConvertCompareFunction(depthStencilState.DepthBufferFunction),
                StencilEnable = depthStencilState.StencilEnable,
                StencilReadMask = (byte)depthStencilState.StencilMask,

                StencilWriteMask = (byte)depthStencilState.StencilWriteMask,
                FrontFace = new DepthStencilOperationDescription
                {
                    StencilFailOp = ConvertStencilOperation(depthStencilState.StencilFail),
                    StencilDepthFailOp = ConvertStencilOperation(depthStencilState.StencilDepthBufferFail),
                    StencilPassOp = ConvertStencilOperation(depthStencilState.StencilPass),
                    StencilFunc = ConvertCompareFunction(depthStencilState.StencilFunction)
                },
                BackFace = new DepthStencilOperationDescription
                {
                    StencilFailOp = ConvertStencilOperation(depthStencilState.CounterClockwiseStencilFail),
                    StencilDepthFailOp = ConvertStencilOperation(depthStencilState.CounterClockwiseStencilDepthBufferFail),
                    StencilPassOp = ConvertStencilOperation(depthStencilState.CounterClockwiseStencilPass),
                    StencilFunc = ConvertCompareFunction(depthStencilState.CounterClockwiseStencilFunction)
                }
            };

            return _d3dDevice.CreateDepthStencilState(depthDesc);
        }

        /// <summary>
        /// 从 FNA RasterizerState 创建 D3D11 RasterizerState
        /// </summary>
        private ID3D11RasterizerState CreateD3D11RasterizerState(RasterizerState rasterizerState)
        {
            if (rasterizerState == null) return null;

            var rasterDesc = new RasterizerDescription
            {
                FillMode = rasterizerState.FillMode == XnaFillMode.Solid ? Vortice.Direct3D11.FillMode.Solid : Vortice.Direct3D11.FillMode.Wireframe,
                CullMode = ConvertCullMode(rasterizerState.CullMode),
                DepthBias = (int)rasterizerState.DepthBias,
                DepthBiasClamp = 0.0f,
                SlopeScaledDepthBias = rasterizerState.SlopeScaleDepthBias,
                DepthClipEnable = true,
                ScissorEnable = rasterizerState.ScissorTestEnable,
                MultisampleEnable = false,
                AntialiasedLineEnable = false
            };

            return _d3dDevice.CreateRasterizerState(rasterDesc);
        }

        // 转换函数
        private static Vortice.Direct3D11.Blend ConvertBlend(XnaBlend fnaBlend)
        {
            return fnaBlend switch
            {
                XnaBlend.Zero => Vortice.Direct3D11.Blend.Zero,
                XnaBlend.One => Vortice.Direct3D11.Blend.One,
                XnaBlend.SourceColor => Vortice.Direct3D11.Blend.SourceColor,
                XnaBlend.InverseSourceColor => Vortice.Direct3D11.Blend.InverseSourceColor,
                XnaBlend.SourceAlpha => Vortice.Direct3D11.Blend.SourceAlpha,
                XnaBlend.InverseSourceAlpha => Vortice.Direct3D11.Blend.InverseSourceAlpha,
                XnaBlend.DestinationColor => Vortice.Direct3D11.Blend.DestinationColor,
                XnaBlend.InverseDestinationColor => Vortice.Direct3D11.Blend.InverseDestinationColor,
                XnaBlend.DestinationAlpha => Vortice.Direct3D11.Blend.DestinationAlpha,
                XnaBlend.InverseDestinationAlpha => Vortice.Direct3D11.Blend.InverseDestinationAlpha,
                XnaBlend.BlendFactor => Vortice.Direct3D11.Blend.BlendFactor,
                XnaBlend.InverseBlendFactor => Vortice.Direct3D11.Blend.InverseBlendFactor,
                XnaBlend.SourceAlphaSaturation => Vortice.Direct3D11.Blend.SourceAlphaSaturate,
                _ => Vortice.Direct3D11.Blend.One
            };
        }

        private static BlendOperation ConvertBlendOperation(XnaBlendFunction fnaFunc)
        {
            return fnaFunc switch
            {
                XnaBlendFunction.Add => BlendOperation.Add,
                XnaBlendFunction.Subtract => BlendOperation.Subtract,
                XnaBlendFunction.ReverseSubtract => BlendOperation.ReverseSubtract,
                XnaBlendFunction.Min => BlendOperation.Min,
                XnaBlendFunction.Max => BlendOperation.Max,
                _ => BlendOperation.Add
            };
        }

        private static ComparisonFunction ConvertCompareFunction(XnaCompareFunction fnaFunc)
        {
            return fnaFunc switch
            {
                XnaCompareFunction.Always => ComparisonFunction.Always,
                XnaCompareFunction.Never => ComparisonFunction.Never,
                XnaCompareFunction.Less => ComparisonFunction.Less,
                XnaCompareFunction.LessEqual => ComparisonFunction.LessEqual,
                XnaCompareFunction.Equal => ComparisonFunction.Equal,
                XnaCompareFunction.NotEqual => ComparisonFunction.NotEqual,
                XnaCompareFunction.Greater => ComparisonFunction.Greater,
                XnaCompareFunction.GreaterEqual => ComparisonFunction.GreaterEqual,
                _ => ComparisonFunction.Always
            };
        }

        private static Vortice.Direct3D11.StencilOperation ConvertStencilOperation(XnaStencilOperation fnaOp)
        {
            return fnaOp switch
            {
                XnaStencilOperation.Keep => Vortice.Direct3D11.StencilOperation.Keep,
                XnaStencilOperation.Zero => Vortice.Direct3D11.StencilOperation.Zero,
                XnaStencilOperation.Replace => Vortice.Direct3D11.StencilOperation.Replace,
                XnaStencilOperation.Increment => Vortice.Direct3D11.StencilOperation.Increment,
                XnaStencilOperation.Decrement => Vortice.Direct3D11.StencilOperation.Decrement,
                XnaStencilOperation.IncrementSaturation => Vortice.Direct3D11.StencilOperation.IncrementSaturate,
                XnaStencilOperation.DecrementSaturation => Vortice.Direct3D11.StencilOperation.DecrementSaturate,
                XnaStencilOperation.Invert => Vortice.Direct3D11.StencilOperation.Invert,
                _ => Vortice.Direct3D11.StencilOperation.Keep
            };
        }

        private static Vortice.Direct3D11.CullMode ConvertCullMode(XnaCullMode fnaCull)
        {
            return fnaCull switch
            {
                XnaCullMode.CullClockwiseFace => Vortice.Direct3D11.CullMode.Front,
                XnaCullMode.CullCounterClockwiseFace => Vortice.Direct3D11.CullMode.Back,
                _ => Vortice.Direct3D11.CullMode.None
            };
        }

        /// <summary>
        /// CPU 端：生成顶点数据，返回指针
        /// </summary>
        public unsafe nint CreateVertexBuffer(Rectangle source, Rectangle dest, int texWidth, int texHeight, float rotation, float depth, uint color, bool flipX, bool flipY)
        {

            var targets = Main.spriteBatch.GraphicsDevice.GetRenderTargets();

            int targetWidth = Main.screenWidth;  // 默认回退值
            int targetHeight = Main.screenHeight;

            // 3. 获取真正的尺寸！
            targetWidth = (targets[0].RenderTarget as RenderTarget2D).Width;
            targetHeight = (targets[0].RenderTarget as RenderTarget2D).Height;


            var material = _currentMaterial as D3D11FCSMaterial;
            if (material.VertexWriter == null)
                return IntPtr.Zero;
            int stride = material.VertexStride;
            int totalBytes = 6 * stride;

            // --- 后端处理深度偏移 ---
            float adjustedDepth = depth - _depthEpsilon;
            _depthEpsilon += EpsilonStep;

            IntPtr tempMem = GlobalVertexPool.RawPool.Rent(totalBytes);
            var writer = material.VertexWriter;

            // 写入微调后的深度
            writer(tempMem, source, dest, texWidth, texHeight, rotation, adjustedDepth, color, flipX, flipY, targetWidth, targetHeight);

            return tempMem;
        }

        public void End()
        {
            try
            {
                if (_currentSortMode != SpriteSortMode.Immediate) ProcessDrawQueue();
            }
            finally
            {
                RestoreDeviceState(in state);
                _isActive = false;
                _drawQueue.Clear();
                _currentMaterial = null;
                _depthEpsilon = 0f;
                GlobalVertexPool.ResetAll();
            }
        }

        /// <summary>
        /// GPU 端：创建 Buffer、InputLayout 并绑定到着色器
        /// </summary>
        public unsafe nint BindVertexLayout(nint vertexDataPtr, int totalBytes, int stride)
        {
            var material = _currentMaterial as D3D11FCSMaterial;

            var desc = new BufferDescription
            {
                ByteWidth = (uint)totalBytes,
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };

            ID3D11Buffer buffer = _d3dDevice.CreateBuffer(desc, new SubresourceData(vertexDataPtr));

            if (material.Effect.Layout.Tag != null)
            {
                _d3dContext.IASetInputLayout(material.Effect.Layout);
            }

            return MarshallingHelpers.ToCallbackPtr(buffer);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFormatSize(string format) => format.ToLower() switch
        {
            "float4" => 16,
            "float3" => 12,
            "float2" => 8,
            "float" => 4,
            "color" => 4,
            _ => 0
        };
    }
}