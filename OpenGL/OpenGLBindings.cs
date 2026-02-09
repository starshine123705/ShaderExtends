using OpenTK;
using SDL2;
using System;
using System.Runtime.InteropServices;

namespace ShaderExtends.OpenGL
{

    public class SDL2BindingsContext : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
        {
            return SDL.SDL_GL_GetProcAddress(procName);
        }
    }
}