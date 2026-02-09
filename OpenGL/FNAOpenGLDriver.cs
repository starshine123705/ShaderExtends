using System;
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

        public FNAOpenGLDriver(GraphicsDevice device)
        {
            _graphicsDevice = device;
            _internalFBO = GL.GenFramebuffer();
        }
        public void Apply(IFCSMaterial material, Texture2D source, RenderTarget2D destination = null, nint vBufPtr = 0, int stride = 0, int count = 3)
        {
            if (!(material is GLFCSMaterial glMat)) return;

            uint srcHandle = OpenGLTextureHelper.GetGLHandle(source);
            if (srcHandle == 0) return;

            int oldProg = GL.GetInteger(GetPName.CurrentProgram);
            int oldFBO = GL.GetInteger(GetPName.DrawFramebufferBinding);
            int oldActiveTex = GL.GetInteger(GetPName.ActiveTexture);
            int[] oldViewport = new int[4]; GL.GetInteger(GetPName.Viewport, oldViewport);
            int oldVAO = GL.GetInteger(GetPName.VertexArrayBinding);

            try
            {
                int finalInputTex = (int)srcHandle;

                if (glMat.GLCS != 0)
                {
                    GL.UseProgram(glMat.GLCS);
                    glMat.SyncToDevice();
                    glMat.EnsureShadow(source.Width, source.Height);
                    var glShadow = (GLShadowBuffer)glMat.Shadow;

                    BindAllUbos(glMat);

                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, (int)srcHandle);
                    GL.BindImageTexture(0, glShadow.GLTexture, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

                    GL.DispatchCompute(glMat.GroupsX, glMat.GroupsY, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);

                    finalInputTex = glShadow.GLTexture;

                    if (glMat.GLProgram == 0 && destination != null)
                    {
                        uint destHandle = OpenGLTextureHelper.GetGLHandle(destination);
                        GL.CopyImageSubData(glShadow.GLTexture, ImageTarget.Texture2D, 0, 0, 0, 0,
                                           (int)destHandle, ImageTarget.Texture2D, 0, 0, 0, 0,
                                           source.Width, source.Height, 1);
                        return;
                    }
                }

                if (glMat.GLProgram != 0)
                {
                    GL.UseProgram(glMat.GLProgram);

                    BindAllSamplers(glMat, finalInputTex);

                    // 2. 目标绑定与视口
                    if (destination != null)
                    {
                        uint destHandle = OpenGLTextureHelper.GetGLHandle(destination);
                        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _internalFBO);
                        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                                              TextureTarget.Texture2D, (int)destHandle, 0);
                        GL.Viewport(0, 0, destination.Width, destination.Height);
                    }
                    else
                    {
                        GL.BindFramebuffer(FramebufferTarget.Framebuffer, oldFBO);
                    }

                    glMat.SyncToDevice();
                    BindAllUbos(glMat);

                    GL.Disable(EnableCap.CullFace);
                    GL.Disable(EnableCap.DepthTest);
                    GL.Disable(EnableCap.Blend);
                    GL.ColorMask(true, true, true, true);

                    if (_emptyVao == -1) _emptyVao = GL.GenVertexArray();
                    GL.BindVertexArray(_emptyVao);
                    GL.VertexAttrib4(0, 1.0f, 1.0f, 1.0f, 1.0f);

                    GL.ClearColor(0, 0, 0, 0);
                    GL.Clear(ClearBufferMask.ColorBufferBit);

                    GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
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

        /// <summary>
        /// 自动根据 Metadata 绑定所有 UBO
        /// </summary>
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

        /// <summary>
        /// 自动扫描并分配采样器单元，确保多纹理逻辑正确
        /// </summary>
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
                    if (loc == -1) continue;

                    GL.Uniform1(loc, texUnit);

                    GL.ActiveTexture(TextureUnit.Texture0 + texUnit);

                    if (texUnit == 0)
                    {
                        GL.BindTexture(TextureTarget.Texture2D, finalInputTex);
                    }
                    else if (texUnit == 1 && glMat.Shadow != null)
                    {
                        var glShadow = (GLShadowBuffer)glMat.Shadow;
                        GL.BindTexture(TextureTarget.Texture2D, glShadow.GLTexture);
                    }

                    texUnit++;
                }
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            float[] borderColor = { 0.0f, 0.0f, 0.0f, 0.0f };
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, borderColor);
        }
        public void Dispose()
        {
            if (_internalFBO != 0) GL.DeleteFramebuffer(_internalFBO);
        }
    }
}