using System;

namespace ShaderExtends.Interfaces
{
    public interface IShadowBuffer : IDisposable
    {
        int Width { get; }
        int Height { get; }
    }
}