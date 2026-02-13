using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Core;
using ShaderExtends.D3D11;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Terraria;
using Buffer = System.Buffer;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;

namespace ShaderExtends.OpenGL
{
    public unsafe class FNAOpenGLDriver : IFNARenderDriver
    {
        private readonly GraphicsDevice _graphicsDevice;
        private int _internalFBO;
        private int _emptyVao = -1;

        private int _dynamicVbo = 0;
        private int _dynamicVao = 0;
        private int _dynamicVboSize = 0;
        private GlStateSnapshot _savedState;

        private bool _isActive = false;
        private IFCSMaterial _currentMaterial;
        private RenderTarget2D _currentDestination;
        private SpriteSortMode _currentSortMode;
        private List<DrawQueueItem> _drawQueue = new();
        private BlendState _currentBlendState;
        private DepthStencilState _currentDepthStencilState;
        private RasterizerState _currentRasterizerState;
        private struct GlStateSnapshot
        {
            public int Fbo;
            public int Vao;
            public int Vbo;
            public int Program;
            public int[] Viewport;
            public bool BlendEnabled;
            public bool DepthEnabled;
            public bool CullEnabled;
            public bool ScissorEnabled;
            public int ActiveTex;
            public int Tex0;
        }

        public IFCSMaterial CurrentMaterial
        {
            get { return _currentMaterial; }
            set { _currentMaterial = value; }
        }


        private float _depthEpsilon = 0f;
        private const float EpsilonStep = 0.000001f;

        private struct DrawCommand
        {
            public uint TextureHandle;
            public Texture2D SourceTexture; // 保持对纹理的引用
            public int RawOffset;
            public int VertexCount;
            public float SortDepth;
            public bool IsVertexless; // 无顶点数据（仅依赖 gl_VertexID）
        }

        private struct GpuBatchCommand
        {
            public uint TextureHandle;
            public Texture2D SourceTexture; // 修改：直接存引用，不再存宽高
            public int StartVertex;
            public int VertexCount;
            public bool IsVertexless; // 无顶点数据批次
        }

        private struct DrawQueueItem
        {
            public Texture2D Source;
            public IntPtr VBufPtr;
            public int Stride;
            public int Count;
            public uint TextureHandle;
            public int OriginalIndex;
        }

        private List<DrawCommand> _drawCommands = new List<DrawCommand>(2048);
        private List<GpuBatchCommand> _gpuBatches = new List<GpuBatchCommand>(128);

        public bool IsActive => _isActive;
        public SpriteSortMode CurrentSortMode => _currentSortMode;

        public FNAOpenGLDriver(GraphicsDevice device)
        {
            _graphicsDevice = device;
            _internalFBO = GL.GenFramebuffer();
            // 移除 _vertexPool 的创建，改用全局静态池
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

            CaptureGlState();
            _isActive = true;
            _currentMaterial = material;
            _currentDestination = destination;
            _currentSortMode = sortMode;
            _currentBlendState = blendState;
            _currentDepthStencilState = depthStencilState;
            _currentRasterizerState = rasterizerState;
            _drawQueue.Clear();

            // 使用全局内存池
            GlobalVertexPool.ResetAll();
        }

        public void Draw(Texture2D source, IntPtr vBufPtr, int stride, int count, float depth)
        {
            if (!_isActive) throw new InvalidOperationException("请先调用 Begin");

            if (_currentSortMode == SpriteSortMode.Immediate)
            {
                ExecuteDrawImmediate(source, vBufPtr, stride, count);
                return;
            }

            // 计算在 RawPool 中的字节偏移
            long offset = (long)vBufPtr - (long)GlobalVertexPool.RawPool.GetBasePtr();

            _drawCommands.Add(new DrawCommand
            {
                TextureHandle = OpenGLTextureHelper.GetGLHandle(source),
                SourceTexture = source,
                RawOffset = (int)offset,
                VertexCount = count,
                SortDepth = depth
            });
        }

        public void Draw(Texture2D source, IntPtr vBufPtr, int stride, int count)
            => Draw(source, vBufPtr, stride, count, 0f);

        public void End()
        {
            if (!_isActive)
                throw new InvalidOperationException("批处理未启动");

            try
            {
                if (_currentSortMode != SpriteSortMode.Immediate)
                {
                    ProcessDrawQueue();
                }
            }
            finally
            {
                RestoreGlState();
                _isActive = false;
                _drawQueue.Clear();
                _drawCommands.Clear();
                _gpuBatches.Clear();
                _currentMaterial = null;
            }
        }

        /// <summary>
        /// Immediate 模式下的立即执行（假设着色器已由外部通过 Material.Apply 设置）
        /// </summary>
        private void ExecuteDrawImmediate(Texture2D source, IntPtr vBufPtr, int stride, int count)
        {
            var material = _currentMaterial as GLFCSMaterial;
            uint finalTex = OpenGLTextureHelper.GetGLHandle(source);

            // --- 1. Compute Shader 处理 (保持原有逻辑) ---
            if (material != null && material.GLCS != 0)
            {
                material.SyncToDevice();
                material.EnsureShadow(source.Width, source.Height);
                var shadow = material.Shadow as GLShadowBuffer;
                if (shadow != null)
                {
                    GL.UseProgram(material.GLCS);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, (int)finalTex);

                    // 绑定材质中的额外纹理到 CS (t1, t2...)
                    if (material.SourceTexture != null)
                    {
                        for (int s = 1; s < material.SourceTexture.Length; s++)
                        {
                            if (material.SourceTexture[s] == null) continue;
                            GL.ActiveTexture(TextureUnit.Texture0 + s);
                            GL.BindTexture(TextureTarget.Texture2D, (int)OpenGLTextureHelper.GetGLHandle(material.SourceTexture[s]));
                        }
                    }

                    GL.BindImageTexture(0, shadow.GLTexture, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);
                    GL.DispatchCompute((uint)material.GroupsX, (uint)material.GroupsY, (uint)material.GroupsZ);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                        finalTex = (uint)shadow.GLTexture;
                }
            }

            // --- 2. 渲染准备 ---
            ApplyGraphicsState();
            GL.UseProgram(material.GLProgram);
            BindAllSamplers(material, (int)finalTex);
            BindAllUbos(material);
            GL.Viewport(0, 0, source.Width, source.Height);

            // --- 3. 核心修改：无顶点适配 ---
            if (vBufPtr != IntPtr.Zero)
            {
                // 有顶点：更新动态缓冲区并绑定 VAO
                UpdateDynamicBuffer((void*)vBufPtr, count * stride, material);
                GL.BindVertexArray(_dynamicVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, count);
            }
            else
            {
                // 无顶点：绑定一个空 VAO (OpenGL Core Profile 要求)
                if (_emptyVao == -1) _emptyVao = GL.GenVertexArray();
                GL.BindVertexArray(_emptyVao);

                // 强制绘制 6 个顶点（全屏矩形）
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }
        }

        private void ProcessDrawQueue()
        {
            if (_drawCommands.Count == 0) return;

            var material = _currentMaterial as GLFCSMaterial;
            int stride = material.VertexStride;

            // 1. 排序：深度优先，纹理次之
            switch (_currentSortMode)
            {
                case SpriteSortMode.Texture:
                    _drawCommands.Sort((a, b) => a.TextureHandle.CompareTo(b.TextureHandle));
                    break;
                case SpriteSortMode.BackToFront:
                    _drawCommands.Sort((a, b) =>
                    {
                        int res = b.SortDepth.CompareTo(a.SortDepth);
                        return res != 0 ? res : a.TextureHandle.CompareTo(b.TextureHandle);
                    });
                    break;
                case SpriteSortMode.FrontToBack:
                    _drawCommands.Sort((a, b) =>
                    {
                        int res = a.SortDepth.CompareTo(b.SortDepth);
                        return res != 0 ? res : a.TextureHandle.CompareTo(b.TextureHandle);
                    });
                    break;
            }

            // 2. 紧凑化搬运与合批分析
            byte* rawBase = (byte*)GlobalVertexPool.RawPool.GetBasePtr();
            byte* sortedBase = (byte*)GlobalVertexPool.SortedPool.GetBasePtr();

            int currentSortedOffset = 0;
            int totalVertexCount = 0;

            var first = _drawCommands[0];
            uint batchTex = first.TextureHandle;
            Texture2D lastTexObj = first.SourceTexture;
            bool batchVertexless = first.IsVertexless;
            int batchStartVertex = 0;
            int batchVertexCount = 0;

            foreach (var cmd in _drawCommands)
            {
                int bytesToCopy = cmd.IsVertexless ? 0 : cmd.VertexCount * stride;

                if (bytesToCopy > 0)
                {
                    int cap = GlobalVertexPool.SortedPool.GetCapacity();
                    int required = currentSortedOffset + bytesToCopy;
                    if (required > cap)
                        throw new InvalidOperationException($"SortedPool overflow: need {required} bytes, capacity {cap}");

                    Buffer.MemoryCopy(
                        rawBase + cmd.RawOffset,
                        sortedBase + currentSortedOffset,
                        cap - currentSortedOffset,
                        bytesToCopy
                    );
                }

                bool sameTexture = cmd.TextureHandle == batchTex;
                bool sameKind = cmd.IsVertexless == batchVertexless;
                if (!(sameTexture && sameKind))
                {
                    _gpuBatches.Add(new GpuBatchCommand
                    {
                        TextureHandle = batchTex,
                        StartVertex = batchStartVertex,
                        VertexCount = batchVertexCount,
                        SourceTexture = lastTexObj,
                        IsVertexless = batchVertexless
                    });

                    batchTex = cmd.TextureHandle;
                    lastTexObj = cmd.SourceTexture;
                    batchVertexless = cmd.IsVertexless;
                    batchStartVertex = totalVertexCount;
                    batchVertexCount = 0;
                }

                currentSortedOffset += bytesToCopy;
                if (!cmd.IsVertexless)
                    totalVertexCount += cmd.VertexCount;
                batchVertexCount += cmd.VertexCount;
            }

            // 扫尾
            _gpuBatches.Add(new GpuBatchCommand
            {
                TextureHandle = batchTex,
                StartVertex = batchStartVertex,
                VertexCount = batchVertexCount,
                SourceTexture = lastTexObj,
                IsVertexless = batchVertexless
            });

            int totalBytesNeeded = totalVertexCount * stride;
            if (totalBytesNeeded > 0)
            {
                UpdateDynamicBuffer(sortedBase, totalBytesNeeded, material);
            }

            RenderBatches(material, totalBytesNeeded > 0);
        }
        public uint GetCurrentRenderTargetTextureID()
        {
            var targets = Main.spriteBatch.GraphicsDevice.GetRenderTargets();
            return OpenGLTextureHelper.GetGLHandle(targets[0].RenderTarget); ;
        }

        private void RenderBatches(GLFCSMaterial material, bool hasVertexData)
        {
            // 1. 基础状态应用
            ApplyGraphicsState();

            // 记录原始 FBO 以便在没有 RenderTarget 时切回
            int oldFBO = GL.GetInteger(GetPName.DrawFramebufferBinding);

            foreach (var batch in _gpuBatches)
            {
                uint finalTex = batch.TextureHandle;

                // --- A. Compute Shader 预处理 ---
                if (material.GLCS != 0)
                {
                    GL.UseProgram(material.GLCS);
                    material.SyncToDevice();

                    material.EnsureShadow(batch.SourceTexture.Width, batch.SourceTexture.Height);
                    var shadow = (GLShadowBuffer)material.Shadow;

                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, (int)batch.TextureHandle);

                    // 绑定材质中的额外纹理到 CS (t1, t2...)
                    if (material.SourceTexture != null)
                    {
                        for (int s = 1; s < material.SourceTexture.Length; s++)
                        {
                            if (material.SourceTexture[s] == null) continue;
                            GL.ActiveTexture(TextureUnit.Texture0 + s);
                            GL.BindTexture(TextureTarget.Texture2D, (int)OpenGLTextureHelper.GetGLHandle(material.SourceTexture[s]));
                        }
                    }

                    GL.BindImageTexture(0, shadow.GLTexture, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

                    GL.DispatchCompute(material.GroupsX, material.GroupsY, material.GroupsZ);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

                    if (material.GLProgram == 0)
                    {
                        GL.CopyImageSubData(shadow.GLTexture, ImageTarget.Texture2D, 0, 0, 0, 0,
                                           (int)GetCurrentRenderTargetTextureID(), ImageTarget.Texture2D, 0, 0, 0, 0,
                                           batch.SourceTexture.Width, batch.SourceTexture.Height, 1);
                    }

                    finalTex = (uint)shadow.GLTexture;
                }

                // --- B. 准备绘制上下文 ---
                GL.UseProgram(material.GLProgram);
                BindAllSamplers(material, (int)finalTex);
                BindAllUbos(material);

                // --- C. 设置渲染目标 (FBO) ---
                if (_currentDestination != null)
                {
                    uint destHandle = OpenGLTextureHelper.GetGLHandle(_currentDestination);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, _internalFBO);
                    GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, (int)destHandle, 0);
                    GL.Viewport(0, 0, _currentDestination.Width, _currentDestination.Height);
                }
                else
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, oldFBO);
                    GL.Viewport(0, 0, _graphicsDevice.PresentationParameters.BackBufferWidth, _graphicsDevice.PresentationParameters.BackBufferHeight);
                }

                // --- D. 执行绘制 ---
                if (batch.IsVertexless)
                {
                    if (_emptyVao == -1) _emptyVao = GL.GenVertexArray();
                    GL.BindVertexArray(_emptyVao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, batch.VertexCount);
                }
                else
                {
                    if (!hasVertexData)
                        continue; // 理论上不会发生，防御性
                    GL.BindVertexArray(_dynamicVao);
                    GL.DrawArrays(PrimitiveType.Triangles, batch.StartVertex, batch.VertexCount);
                }
            }

            // 绘制完成后切回原始 FBO
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, oldFBO);
        }

        private void BindAllUbos(GLFCSMaterial glMat)
        {
            foreach (var buffer in glMat.Effect.Metadata.Buffers)
            {
                int ubo = glMat.GetUbo(buffer.Slot);
                if (ubo != 0)
                {
                    GL.BindBufferBase(BufferRangeTarget.UniformBuffer, buffer.Slot, ubo);
                }
            }
        }

        private void BindAllSamplers(GLFCSMaterial glMat, int finalInputTex)
        {
            int prog = glMat.GLProgram;
            int[] progParams = new int[1];
            GL.GetProgram(prog, GetProgramParameterName.ActiveUniforms, progParams);
            int uniformCount = progParams[0];

            int texUnit = 0;
            for (int i = 0; i < uniformCount; i++)
            {
                string name = GL.GetActiveUniform(prog, i, out _, out ActiveUniformType type);
                if (type == ActiveUniformType.Sampler2D)
                {
                    int loc = GL.GetUniformLocation(prog, name);
                    GL.ActiveTexture(TextureUnit.Texture0 + texUnit);

                    int texHandle = 0;
                    if (texUnit == 0)
                    {
                        texHandle = finalInputTex; // 主纹理绑定到 t0
                    }
                    else if (glMat.SourceTexture != null && texUnit < glMat.SourceTexture.Length && glMat.SourceTexture[texUnit] != null)
                    {
                        texHandle = (int)OpenGLTextureHelper.GetGLHandle(glMat.SourceTexture[texUnit]); // 额外纹理绑定到 t1, t2...
                    }

                    GL.BindTexture(TextureTarget.Texture2D, texHandle);
                    GL.Uniform1(loc, texUnit);
                    texUnit++;
                }
            }
        }

        private void ApplyGraphicsState()
        {
            var blend = _currentBlendState ?? BlendState.Opaque;
            var depth = _currentDepthStencilState ?? DepthStencilState.Default;
            var raster = _currentRasterizerState ?? RasterizerState.CullCounterClockwise;

            ApplyBlendState(blend);
            ApplyDepthStencilState(depth);
            ApplyRasterizerState(raster);
            GL.Disable(EnableCap.ScissorTest);
        }

        private void ApplyBlendState(BlendState blend)
        {
            if (blend.AlphaBlendFunction == BlendFunction.Add &&
                blend.ColorBlendFunction == BlendFunction.Add &&
                blend.ColorSourceBlend == Blend.One &&
                blend.ColorDestinationBlend == Blend.Zero &&
                blend.AlphaSourceBlend == Blend.One &&
                blend.AlphaDestinationBlend == Blend.Zero)
            {
                GL.Disable(EnableCap.Blend);
            }
            else
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendColor(
                    blend.BlendFactor.R / 255f,
                    blend.BlendFactor.G / 255f,
                    blend.BlendFactor.B / 255f,
                    blend.BlendFactor.A / 255f);

                GL.BlendEquationSeparate(
                    ConvertBlendEquation(blend.ColorBlendFunction),
                    ConvertBlendEquation(blend.AlphaBlendFunction));

                GL.BlendFuncSeparate(
                    ConvertBlendFactorSrc(blend.ColorSourceBlend),
                    ConvertBlendFactorDest(blend.ColorDestinationBlend),
                    ConvertBlendFactorSrc(blend.AlphaSourceBlend),
                    ConvertBlendFactorDest(blend.AlphaDestinationBlend));
            }

            ApplyColorWriteMask(blend.ColorWriteChannels);
        }

        private BlendingFactorSrc ConvertBlendFactorSrc(Blend blend) => blend switch
        {
            Blend.Zero => BlendingFactorSrc.Zero,
            Blend.One => BlendingFactorSrc.One,
            Blend.SourceColor => BlendingFactorSrc.SrcColor,
            Blend.InverseSourceColor => BlendingFactorSrc.OneMinusSrcColor,
            Blend.SourceAlpha => BlendingFactorSrc.SrcAlpha,
            Blend.InverseSourceAlpha => BlendingFactorSrc.OneMinusSrcAlpha,
            Blend.DestinationColor => BlendingFactorSrc.DstColor,
            Blend.InverseDestinationColor => BlendingFactorSrc.OneMinusDstColor,
            Blend.DestinationAlpha => BlendingFactorSrc.DstAlpha,
            Blend.InverseDestinationAlpha => BlendingFactorSrc.OneMinusDstAlpha,
            Blend.BlendFactor => BlendingFactorSrc.ConstantColor,
            Blend.InverseBlendFactor => BlendingFactorSrc.OneMinusConstantColor,
            Blend.SourceAlphaSaturation => BlendingFactorSrc.SrcAlphaSaturate,
            _ => BlendingFactorSrc.One
        };

        private BlendingFactorDest ConvertBlendFactorDest(Blend blend) => blend switch
        {
            Blend.Zero => BlendingFactorDest.Zero,
            Blend.One => BlendingFactorDest.One,
            Blend.SourceColor => BlendingFactorDest.SrcColor,
            Blend.InverseSourceColor => BlendingFactorDest.OneMinusSrcColor,
            Blend.SourceAlpha => BlendingFactorDest.SrcAlpha,
            Blend.InverseSourceAlpha => BlendingFactorDest.OneMinusSrcAlpha,
            Blend.DestinationColor => BlendingFactorDest.DstColor,
            Blend.InverseDestinationColor => BlendingFactorDest.OneMinusDstColor,
            Blend.DestinationAlpha => BlendingFactorDest.DstAlpha,
            Blend.InverseDestinationAlpha => BlendingFactorDest.OneMinusDstAlpha,
            Blend.BlendFactor => BlendingFactorDest.ConstantColor,
            Blend.InverseBlendFactor => BlendingFactorDest.OneMinusConstantColor,
            _ => BlendingFactorDest.One
        };

        private void ApplyDepthStencilState(DepthStencilState depth)
        {
            if (depth.DepthBufferEnable)
            {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthMask(depth.DepthBufferWriteEnable);
                GL.DepthFunc(ConvertDepthFunc(depth.DepthBufferFunction));
            }
            else
            {
                GL.Disable(EnableCap.DepthTest);
            }

            if (depth.StencilEnable)
            {
                GL.Enable(EnableCap.StencilTest);
                GL.StencilMask(depth.StencilWriteMask);
                GL.StencilFunc(ConvertStencilFunc(depth.StencilFunction), depth.ReferenceStencil, depth.StencilMask);
                GL.StencilOp(
                    ConvertStencilOp(depth.StencilFail),
                    ConvertStencilOp(depth.StencilDepthBufferFail),
                    ConvertStencilOp(depth.StencilPass));
            }
            else
            {
                GL.Disable(EnableCap.StencilTest);
            }
        }

        private void ApplyRasterizerState(RasterizerState raster)
        {
            if (raster.CullMode == CullMode.None)
            {
                GL.Disable(EnableCap.CullFace);
            }
            else
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(raster.CullMode == CullMode.CullClockwiseFace ? TriangleFace.Front : TriangleFace.Back);
            }

            GL.PolygonMode(TriangleFace.FrontAndBack,
                raster.FillMode == FillMode.WireFrame ? PolygonMode.Line : PolygonMode.Fill);

            if (raster.ScissorTestEnable)
                GL.Enable(EnableCap.ScissorTest);
            else
                GL.Disable(EnableCap.ScissorTest);
        }

        private void ApplyColorWriteMask(ColorWriteChannels channels)
        {
            bool r = (channels & ColorWriteChannels.Red) != 0;
            bool g = (channels & ColorWriteChannels.Green) != 0;
            bool b = (channels & ColorWriteChannels.Blue) != 0;
            bool a = (channels & ColorWriteChannels.Alpha) != 0;
            GL.ColorMask(r, g, b, a);
        }

        private BlendEquationMode ConvertBlendEquation(BlendFunction func) => func switch
        {
            BlendFunction.Add => BlendEquationMode.FuncAdd,
            BlendFunction.Subtract => BlendEquationMode.FuncSubtract,
            BlendFunction.ReverseSubtract => BlendEquationMode.FuncReverseSubtract,
            BlendFunction.Min => BlendEquationMode.Min,
            BlendFunction.Max => BlendEquationMode.Max,
            _ => BlendEquationMode.FuncAdd
        };

        private DepthFunction ConvertDepthFunc(CompareFunction func) => func switch
        {
            CompareFunction.Always => DepthFunction.Always,
            CompareFunction.Never => DepthFunction.Never,
            CompareFunction.Less => DepthFunction.Less,
            CompareFunction.LessEqual => DepthFunction.Lequal,
            CompareFunction.Equal => DepthFunction.Equal,
            CompareFunction.NotEqual => DepthFunction.Notequal,
            CompareFunction.Greater => DepthFunction.Greater,
            CompareFunction.GreaterEqual => DepthFunction.Gequal,
            _ => DepthFunction.Always
        };

        private StencilFunction ConvertStencilFunc(CompareFunction func) => func switch
        {
            CompareFunction.Always => StencilFunction.Always,
            CompareFunction.Never => StencilFunction.Never,
            CompareFunction.Less => StencilFunction.Less,
            CompareFunction.LessEqual => StencilFunction.Lequal,
            CompareFunction.Equal => StencilFunction.Equal,
            CompareFunction.NotEqual => StencilFunction.Notequal,
            CompareFunction.Greater => StencilFunction.Greater,
            CompareFunction.GreaterEqual => StencilFunction.Gequal,
            _ => StencilFunction.Always
        };

        private StencilOp ConvertStencilOp(StencilOperation op) => op switch
        {
            StencilOperation.Keep => StencilOp.Keep,
            StencilOperation.Zero => StencilOp.Zero,
            StencilOperation.Replace => StencilOp.Replace,
            StencilOperation.Increment => StencilOp.Incr,
            StencilOperation.Decrement => StencilOp.Decr,
            StencilOperation.IncrementSaturation => StencilOp.IncrWrap,
            StencilOperation.DecrementSaturation => StencilOp.DecrWrap,
            StencilOperation.Invert => StencilOp.Invert,
            _ => StencilOp.Keep
        };

        public void Dispose()
        {
            if (_internalFBO != 0)
                GL.DeleteFramebuffer(_internalFBO);
            if (_emptyVao != -1)
                GL.DeleteVertexArray(_emptyVao);

            // 全局内存池由应用程序全局管理释放
        }

        /// <summary>
        /// CPU 端：从内存池租借内存，填充顶点数据
        /// </summary>
        public unsafe nint CreateVertexBuffer(Rectangle source, Rectangle dest, int texWidth, int texHeight, float rotation, float depth, uint color, bool flipX, bool flipY)
        {
            var targets = Main.spriteBatch.GraphicsDevice.GetRenderTargets();

            int targetWidth = Main.screenWidth;  // 默认回退值
            int targetHeight = Main.screenHeight;

            // 3. 获取真正的尺寸！
            targetWidth = (targets[0].RenderTarget as RenderTarget2D).Width;
            targetHeight = (targets[0].RenderTarget as RenderTarget2D).Height;

            var material = _currentMaterial as GLFCSMaterial;
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


        /// <summary>
        /// GPU 端：创建 VBO、VAO 并绑定到着色器
        /// </summary>
        private void UpdateDynamicBuffer(void* data, int size, GLFCSMaterial material)
        {
            if (_dynamicVbo == 0)
            {
                _dynamicVbo = GL.GenBuffer();
                _dynamicVao = GL.GenVertexArray();
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynamicVbo);

            if (size > _dynamicVboSize)
            {
                // 扩容：使用 BufferData 分配新空间
                _dynamicVboSize = size + 1024; // 预留一点缓冲避免频繁扩容
                GL.BufferData(BufferTarget.ArrayBuffer, _dynamicVboSize, (IntPtr)data, BufferUsageHint.DynamicDraw);

                // 只有在扩容或初次创建时，才需要重新配置 VAO 属性
                GL.BindVertexArray(_dynamicVao);
                var elements = material.Effect.Metadata.InputElements;
                for (int i = 0; i < elements.Count; i++)
                {
                    var e = elements[i];
                    GL.EnableVertexAttribArray(i);
                    // 注意：这里使用的是传统的 VertexAttribPointer，因为它直接作用于当前绑定的 ArrayBuffer
                    GL.VertexAttribPointer(i, GetElementCount(e.Format), VertexAttribPointerType.Float, false, material.VertexStride, e.AlignedByteOffset);
                }
            }
            else
            {
                // Orphanage 优化：传入 NULL 告诉驱动不再引用旧内存，防止管线阻塞
                GL.BufferData(BufferTarget.ArrayBuffer, _dynamicVboSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                // 上传新数据
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, size, (IntPtr)data);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetFormatSize(string format) => format.ToLower() switch
        {
            "float4" => 16,
            "float3" => 12,
            "float2" => 8,
            "float" => 4,
            "color" => 4,
            _ => 0
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetElementCount(string format) => format.ToLower() switch
        {
            "float4" => 4,
            "float3" => 3,
            "float2" => 2,
            "color" => 4,
            _ => 0
        };
        private void CaptureGlState()
        {
            _savedState.Fbo = GL.GetInteger(GetPName.DrawFramebufferBinding);
            _savedState.Vao = GL.GetInteger(GetPName.VertexArrayBinding);
            _savedState.Vbo = GL.GetInteger(GetPName.ArrayBufferBinding);
            _savedState.Program = GL.GetInteger(GetPName.CurrentProgram);
            _savedState.Viewport = new int[4];
            GL.GetInteger(GetPName.Viewport, _savedState.Viewport);
            _savedState.BlendEnabled = GL.IsEnabled(EnableCap.Blend);
            _savedState.DepthEnabled = GL.IsEnabled(EnableCap.DepthTest);
            _savedState.CullEnabled = GL.IsEnabled(EnableCap.CullFace);
            _savedState.ScissorEnabled = GL.IsEnabled(EnableCap.ScissorTest);
            GL.GetInteger(GetPName.ActiveTexture, out _savedState.ActiveTex);
            GL.ActiveTexture(TextureUnit.Texture0);
            _savedState.Tex0 = GL.GetInteger(GetPName.TextureBinding2D);
            GL.ActiveTexture((TextureUnit)_savedState.ActiveTex);
        }

        private void RestoreGlState()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _savedState.Fbo);
            GL.BindVertexArray(_savedState.Vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _savedState.Vbo);
            GL.UseProgram(_savedState.Program);
            GL.Viewport(_savedState.Viewport[0], _savedState.Viewport[1], _savedState.Viewport[2], _savedState.Viewport[3]);
            SetEnable(EnableCap.Blend, _savedState.BlendEnabled);
            SetEnable(EnableCap.DepthTest, _savedState.DepthEnabled);
            SetEnable(EnableCap.CullFace, _savedState.CullEnabled);
            SetEnable(EnableCap.ScissorTest, _savedState.ScissorEnabled);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _savedState.Tex0);
            GL.ActiveTexture((TextureUnit)_savedState.ActiveTex);
        }

        private static void SetEnable(EnableCap cap, bool on)
        {
            if (on) GL.Enable(cap); else GL.Disable(cap);
        }
    }
}
 