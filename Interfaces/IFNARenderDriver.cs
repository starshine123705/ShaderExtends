using Microsoft.Xna.Framework.Graphics;
using System;

namespace ShaderExtends.Interfaces
{
    public interface IFNARenderDriver : IDisposable
    {
        void Apply(IFCSMaterial material, Texture2D source, RenderTarget2D destination = null, IntPtr vBufPtr = default, int stride = 0, int count = 3);
    }
}