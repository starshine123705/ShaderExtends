using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace ShaderExtends.OpenGL
{
    [StructLayout(LayoutKind.Explicit)]
    public struct OpenGLTexture
    {
        [FieldOffset(0)]
        public uint Handle;             // GLuint (4 bytes)

        [FieldOffset(4)]
        public uint Target;             // GLenum (4 bytes)

        [FieldOffset(8)]
        public byte HasMipmaps;         // uint8_t (1 byte)

        [FieldOffset(12)]
        public uint WrapS;              // Enum (4 bytes) 

        [FieldOffset(16)]
        public uint WrapT;              // Enum (4 bytes)

        [FieldOffset(20)]
        public uint WrapR;              // Enum (4 bytes)

        [FieldOffset(24)]
        public uint Filter;             // Enum (4 bytes)

        [FieldOffset(28)]
        public float Anisotropy;        // GLfloat (4 bytes)

        [FieldOffset(32)]
        public int MaxMipmapLevel;      // int32_t (4 bytes)

        [FieldOffset(36)]
        public float LodBias;           // float (4 bytes)

        [FieldOffset(40)]
        public uint Format;             // Enum (4 bytes)

        // Union Start (Offset 44)
        [FieldOffset(44)]
        public int Width;

        [FieldOffset(48)]
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGLRenderer
    {
        public IntPtr ParentDevice;
        public IntPtr SDL_GLContext;
        public byte UseES3;
        public byte UseCoreProfile;
        // ... 后续字段
    }

    public static class OpenGLTextureHelper
    {
        public static uint GetGLHandle(Texture texture)
        {
            if (texture == null) return 0;

            var field = typeof(Texture).GetField("texture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            IntPtr fnaTexturePtr = (IntPtr)field.GetValue(texture);

            if (fnaTexturePtr == IntPtr.Zero) return 0;

            return (uint)Marshal.ReadInt32(fnaTexturePtr);
        }

        public static (int w, int h) GetGLTextureSize(Texture texture)
        {
            var field = typeof(Texture).GetField("texture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            IntPtr fnaTexturePtr = (IntPtr)field.GetValue(texture);

            var texInfo = Marshal.PtrToStructure<OpenGLTexture>(fnaTexturePtr);
            return (texInfo.Width, texInfo.Height);
        }
    }
}