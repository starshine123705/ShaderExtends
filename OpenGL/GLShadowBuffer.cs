using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Interfaces;
using System;

namespace ShaderExtends.OpenGL
{
    public class GLShadowBuffer : IShadowBuffer
    {
        public int GLTexture { get; private set; }
        public int Width { get; }
        public int Height { get; }

        public GLShadowBuffer(int width, int height)
        {
            Width = width;
            Height = height;
            GLTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, GLTexture);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                         width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        public void Dispose()
        {
            if (GLTexture != 0) GL.DeleteTexture(GLTexture);
        }
    }
}