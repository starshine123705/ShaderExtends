using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Interfaces;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;

namespace ShaderExtends.OpenGL
{
    public unsafe class FNAOpenGLDriver : IFNARenderDriver
    {
        private readonly GraphicsDevice _graphicsDevice;
        private int _internalFBO;
        private int _emptyVao = -1;

        private bool _isActive = false;
        private IFCSMaterial _currentMaterial;
        private RenderTarget2D _currentDestination;
        private SpriteSortMode _currentSortMode;
        private List<DrawQueueItem> _drawQueue = new();
        private BlendState _currentBlendState;
        private DepthStencilState _currentDepthStencilState;
        private RasterizerState _currentRasterizerState;

        private struct DrawQueueItem
        {
            public Texture2D Source;
            public IntPtr VBufPtr;
            public int Stride;
            public int Count;
            public uint TextureHandle;
            public int OriginalIndex;
        }

        public bool IsActive => _isActive;
        public SpriteSortMode CurrentSortMode => _currentSortMode;

        public FNAOpenGLDriver(GraphicsDevice device)
        {
            _graphicsDevice = device;
            _internalFBO = GL.GenFramebuffer();
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
            _currentDepthStencilState = depthStencilState;
            _currentRasterizerState = rasterizerState;
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
            uint srcHandle = OpenGLTextureHelper.GetGLHandle(source);
            if (srcHandle == 0) return;

            ApplyGraphicsState();

            if (_emptyVao == -1) _emptyVao = GL.GenVertexArray();
            GL.BindVertexArray(_emptyVao);
            GL.VertexAttrib4(0, 1.0f, 1.0f, 1.0f, 1.0f);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, (int)srcHandle);
            GL.Viewport(0, 0, source.Width, source.Height);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        private void ProcessDrawQueue()
        {
            if (_drawQueue.Count == 0)
                return;

            var material = _currentMaterial as GLFCSMaterial;
            if (material == null) return;

            // 1. 获取纹理Handle
            for (int i = 0; i < _drawQueue.Count; i++)
            {
                var item = _drawQueue[i];
                item.TextureHandle = OpenGLTextureHelper.GetGLHandle(item.Source);
                _drawQueue[i] = item;
            }

            // 2. 按排序模式排序
            if (_currentSortMode == SpriteSortMode.BackToFront)
            {
                _drawQueue.Reverse();
            }
            // Deferred 保持调用顺序

            int oldProg = GL.GetInteger(GetPName.CurrentProgram);
            int oldFBO = GL.GetInteger(GetPName.DrawFramebufferBinding);
            int oldActiveTex = GL.GetInteger(GetPName.ActiveTexture);
            int[] oldViewport = new int[4];
            GL.GetInteger(GetPName.Viewport, oldViewport);
            int oldVAO = GL.GetInteger(GetPName.VertexArrayBinding);

            try
            {
                ApplyGraphicsState();

                if (_emptyVao == -1) _emptyVao = GL.GenVertexArray();
                GL.BindVertexArray(_emptyVao);
                GL.VertexAttrib4(0, 1.0f, 1.0f, 1.0f, 1.0f);

                // 4. 按纹理分批执行
                uint currentTexture = 0;

                foreach (var item in _drawQueue)
                {
                    // 仅在纹理改变时绑定
                    if (item.TextureHandle != currentTexture)
                    {
                        currentTexture = item.TextureHandle;

                        // Compute Shader 阶段
                        if (material.GLCS != 0)
                        {
                            GL.UseProgram(material.GLCS);
                            material.SyncToDevice();
                            material.EnsureShadow(item.Source.Width, item.Source.Height);
                            var glShadow = (GLShadowBuffer)material.Shadow;

                            BindAllUbos(material);

                            GL.ActiveTexture(TextureUnit.Texture0);
                            GL.BindTexture(TextureTarget.Texture2D, (int)item.TextureHandle);
                            GL.BindImageTexture(0, glShadow.GLTexture, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

                            GL.DispatchCompute(material.GroupsX, material.GroupsY, 1);
                            GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);

                            currentTexture = (uint)glShadow.GLTexture;
                        }
                    }

                    // Vertex/Pixel Shader 阶段
                    if (material.GLProgram != 0)
                    {
                        GL.UseProgram(material.GLProgram);
                        BindAllSamplers(material, (int)currentTexture);

                        GL.Viewport(0, 0, item.Source.Width, item.Source.Height);

                        if (_currentDestination != null)
                        {
                            uint destHandle = OpenGLTextureHelper.GetGLHandle(_currentDestination);
                            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _internalFBO);
                            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                                                  TextureTarget.Texture2D, (int)destHandle, 0);
                        }
                        else
                        {
                            GL.BindFramebuffer(FramebufferTarget.Framebuffer, oldFBO);
                        }

                        material.SyncToDevice();
                        BindAllUbos(material);

                        GL.ClearColor(0, 0, 0, 0);
                        GL.Clear(ClearBufferMask.ColorBufferBit);

                        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < 8; i++)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }

                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, 0);
                if (oldVAO != -1) GL.BindVertexArray(oldVAO);
                GL.UseProgram(oldProg);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, oldFBO);
                GL.ActiveTexture((TextureUnit)oldActiveTex);
                GL.Viewport(oldViewport[0], oldViewport[1], oldViewport[2], oldViewport[3]);
            }
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
                    GL.BindTexture(TextureTarget.Texture2D, name == "InputTexture" ? finalInputTex : 0);
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
        }

        public nint CreateVertexBuffer(float[] data)
        {
            return 0;
        }
    }
}