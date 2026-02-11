using Microsoft.Xna.Framework;
using XnaBlend = Microsoft.Xna.Framework.Graphics.Blend;
using XnaBlendFunction = Microsoft.Xna.Framework.Graphics.BlendFunction;
using XnaCompareFunction = Microsoft.Xna.Framework.Graphics.CompareFunction;
using XnaStencilOperation = Microsoft.Xna.Framework.Graphics.StencilOperation;
using XnaCullMode = Microsoft.Xna.Framework.Graphics.CullMode;
using XnaFillMode = Microsoft.Xna.Framework.Graphics.FillMode;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Blend = Vortice.Direct3D11.Blend;
using TextureAddressMode = Vortice.Direct3D11.TextureAddressMode;
using Viewport = Vortice.Mathematics.Viewport;
using Microsoft.Xna.Framework.Graphics;

namespace ShaderExtends.D3D11
{
    public unsafe class FNAD3D11Driver : IDisposable, IFNARenderDriver
    {
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

        private struct DrawQueueItem
        {
            public Texture2D Source;
            public IntPtr VBufPtr;
            public int Stride;
            public int Count;
            public IntPtr TextureHandle;
        }

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

            _isActive = true;
            _currentMaterial = material;
            _currentDestination = destination;
            _currentSortMode = sortMode;
            _currentBlendState = blendState;
            _currentRasterizerState = rasterizerState;
            _currentDepthStencilState = depthStencilState;
            _drawQueue.Clear();
        }

        public void Draw(
            Texture2D source,
            IntPtr vBufPtr = default,
            int stride = 0,
            int count = 3)
        {
            if (!_isActive)
                throw new InvalidOperationException("批处理未启动，请先调用 Begin()");

            if (_currentSortMode == SpriteSortMode.Immediate)
            {
                // Immediate 模式：立即执行（着色器由外部 Material.Apply 设置）
                ExecuteDrawImmediate(source, vBufPtr, stride, count);
            }
            else
            {
                // Deferred/BackToFront 模式：加入队列
                _drawQueue.Add(new DrawQueueItem
                {
                    Source = source,
                    VBufPtr = vBufPtr,
                    Stride = stride,
                    Count = count
                });
            }
        }

        public void End()
        {
            if (!_isActive)
                throw new InvalidOperationException("批处理未启动");

            try
            {
                // Immediate 模式已在 Draw 时执行，无需处理队列
                if (_currentSortMode != SpriteSortMode.Immediate)
                {
                    ProcessDrawQueue();
                }
            }
            finally
            {
                _isActive = false;
                _drawQueue.Clear();
                _currentMaterial = null;
            }
        }

        /// <summary>
        /// Immediate 模式下的立即执行（假设着色器已由外部通过 Material.Apply 设置）
        /// </summary>
        private void ExecuteDrawImmediate(Texture2D source, IntPtr vBufPtr, int stride, int count)
        {
            IntPtr sourceSRVPtr = D3D11TextureHelper.GetD3D11SRV_Method2(source);
            var inputSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(sourceSRVPtr);
            
            _d3dContext.PSSetShaderResource(0, inputSRV);
            _d3dContext.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, source.Width, source.Height));

            if (vBufPtr != IntPtr.Zero)
            {
                var vBuf = MarshallingHelpers.FromPointer<ID3D11Buffer>(vBufPtr);
                _d3dContext.IASetVertexBuffer(0, vBuf, (uint)stride, 0);
            }

            _d3dContext.Draw((uint)count, 0);
        }

        private void ProcessDrawQueue()
        {
            if (_drawQueue.Count == 0)
                return;

            var material = _currentMaterial as D3D11FCSMaterial;
            if (material == null) return;

            // 1. 获取纹理Handle
            for (int i = 0; i < _drawQueue.Count; i++)
            {
                var item = _drawQueue[i];
                item.TextureHandle = D3D11TextureHelper.GetD3D11SRV_Method2(item.Source);
                _drawQueue[i] = item;
            }

            // 2. 按排序模式排序
            if (_currentSortMode == SpriteSortMode.BackToFront)
            {
                _drawQueue.Reverse();
            }
            // Deferred 保持调用顺序

            SaveDeviceState(out var savedState);

            try
            {
                material.SyncToDevice(_d3dContext);

                // 3. 应用图形状态
                ApplyGraphicsState();

                _d3dContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
                _d3dContext.VSSetShader(material.Effect.VS);
                _d3dContext.PSSetShader(material.Effect.PS);
                _d3dContext.IASetInputLayout(material.Effect.Layout.Tag == null ? null : material.Effect.Layout);

                // 一次性设置常量缓冲
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

                // 4. 按纹理分批，减少纹理切换
                IntPtr currentTexture = IntPtr.Zero;

                foreach (var item in _drawQueue)
                {
                    // 仅在纹理改变时绑定（关键优化）
                    if (item.TextureHandle != currentTexture)
                    {
                        currentTexture = item.TextureHandle;
                        var currentSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(item.TextureHandle);
                        _d3dContext.PSSetShaderResource(0, currentSRV);
                    }

                    // 设置视口
                    _d3dContext.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, item.Source.Width, item.Source.Height));

                    // 仅在顶点缓冲改变时重新绑定（关键优化）
                    if (item.VBufPtr != IntPtr.Zero)
                    {
                        var vBuf = MarshallingHelpers.FromPointer<ID3D11Buffer>(item.VBufPtr);
                        _d3dContext.IASetVertexBuffer(0, vBuf, (uint)item.Stride, 0);
                    }

                    _d3dContext.Draw((uint)item.Count, 0);
                }
            }
            finally
            {
                _d3dContext.PSSetShaderResource(0, null);
                _d3dContext.IASetVertexBuffers(0, 1, new ID3D11Buffer[] { null }, new uint[] { 0 }, new uint[] { 0 });
                _d3dContext.VSSetConstantBuffers(0, 8, new ID3D11Buffer[8]);
                _d3dContext.PSSetConstantBuffers(0, 8, new ID3D11Buffer[8]);
                _d3dContext.CSSetShader(null);
                _d3dContext.Flush();

                RestoreDeviceState(in savedState);
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

        public nint CreateVertexBuffer(float[] data)
        {
            return 0
                ;
        }
    }
}