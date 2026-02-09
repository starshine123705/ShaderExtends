using ShaderExtends.D3D11;
using ShaderExtends.OpenGL;
using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;

public class FNARenderContext : IDisposable
{
    private readonly IFNARenderDriver _driver;

    public FNARenderContext(GraphicsDevice device)
    {
        var backend = BackendInterop.GetBackendPointers(device);

        if (backend.rendererType == FNA3D_SysRendererType.D3D11)
        {
            _driver = new FNAD3D11Driver(device, backend);
        }
        else
        {
            GL.LoadBindings(new SDL2BindingsContext());
            string vendor = OpenTK.Graphics.OpenGL4.GL.GetString(OpenTK.Graphics.OpenGL4.StringName.Vendor);
            Console.WriteLine($"OpenTK 已挂载到 FNA 上下文! 显卡厂商: {vendor}");
            _driver = new FNAOpenGLDriver(device);
        }
    }

    public void Apply(IFCSMaterial material, Texture2D source, RenderTarget2D destination = null, IntPtr vBufPtr = default, int stride = 0, int count = 3)
    {
        _driver.Apply(material, source, destination, vBufPtr, stride, count);
    }

    public void Dispose() => _driver.Dispose();
}