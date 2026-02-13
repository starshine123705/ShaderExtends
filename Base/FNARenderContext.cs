using ShaderExtends.D3D11;
using ShaderExtends.OpenGL;
using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using Microsoft.Xna.Framework;
using Color = Microsoft.Xna.Framework.Color;

public class FNARenderContext : IDisposable
{
    private readonly IFNARenderDriver _driver;

    public IFNARenderDriver GetFNARenderDriver() => _driver;

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

    /// <summary>
    /// 开始批处理
    /// </summary>
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
        _driver.Begin(destination, sortMode, blendState, samplerState, depthStencilState, rasterizerState, transformMatrix, material);
    }
    public void Draw(Texture2D texture, Vector2 position, Color color)
    {
        var source = new Rectangle(0, 0, texture.Width, texture.Height);
        var dest = new Rectangle((int)position.X, (int)position.Y, texture.Width, texture.Height);
        nint vBufPtr = _driver.CreateVertexBuffer(source, dest, texture.Width, texture.Height, 0f, 0f, color.PackedValue, false, false);
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, _driver.CurrentMaterial.VertexStride, 6, 0f);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, 0, 6, 0f);
        }
    }

    public void Draw(Texture2D texture, Rectangle destinationRectangle, Color color)
    {
        var source = new Rectangle(0, 0, texture.Width, texture.Height);
        nint vBufPtr = _driver.CreateVertexBuffer(source, destinationRectangle, texture.Width, texture.Height, 0f, 0f, color.PackedValue, false, false); 
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, _driver.CurrentMaterial.VertexStride, 6, 0f);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, 0, 6, 0f);
        }
    }

    public void Draw(Texture2D texture, Rectangle sourceRectangle, Rectangle destinationRectangle, Color color)
    {
        nint vBufPtr = _driver.CreateVertexBuffer(sourceRectangle, destinationRectangle, texture.Width, texture.Height, 0f, 0f, color.PackedValue, false, false);
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, _driver.CurrentMaterial.VertexStride, 6, 0f);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, 0, 6, 0f);
        }
    }

    public void Draw(Texture2D texture, Vector2 position, Rectangle sourceRectangle, float size, float rotation, Color color, SpriteEffects effects, float depth)
    {
        var dest = new Rectangle(
            (int)position.X,
            (int)position.Y,
            (int)(sourceRectangle.Width * size),
            (int)(sourceRectangle.Height * size)
        );
        bool flipX = (effects & SpriteEffects.FlipHorizontally) != 0;
        bool flipY = (effects & SpriteEffects.FlipVertically) != 0;
        nint vBufPtr = _driver.CreateVertexBuffer(sourceRectangle, dest, texture.Width, texture.Height, rotation, depth, color.PackedValue, flipX, flipY);
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, _driver.CurrentMaterial.VertexStride, 6, depth);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, 0, 6, depth);
        }
    }

    public void Draw(Texture2D texture, Rectangle sourceRectangle, Rectangle destinationRectangle, float size, float rotation, Color color, SpriteEffects effects, float depth)
    {
        var dest = new Rectangle(
            destinationRectangle.X,
            destinationRectangle.Y,
            (int)(destinationRectangle.Width * size),
            (int)(destinationRectangle.Height * size)
        );
        bool flipX = (effects & SpriteEffects.FlipHorizontally) != 0;
        bool flipY = (effects & SpriteEffects.FlipVertically) != 0;
        nint vBufPtr = _driver.CreateVertexBuffer(sourceRectangle, dest, texture.Width, texture.Height, rotation, depth, color.PackedValue, flipX, flipY); 
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, _driver.CurrentMaterial.VertexStride, 6, depth);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, 0, 6, depth);
        }
    }

    /// <summary>
    /// 添加绘制项
    /// </summary>
    public void Draw(
        Texture2D source,
        IntPtr vBufPtr = default,
        int stride = 0,
        int count = 3)
    {
        _driver.Draw(source, vBufPtr, stride, count);
    }

    /// <summary>
    /// 结束批处理
    /// </summary>
    public void End()
    {
        _driver.End();
    }

    /// <summary>
    /// 检查是否在批处理中
    /// </summary>
    public bool IsActive => _driver.IsActive;

    /// <summary>
    /// 获取当前排序模式
    /// </summary>
    public SpriteSortMode CurrentSortMode => _driver.CurrentSortMode;

    /// <summary>
    /// 旧的单次应用接口（向后兼容）
    /// </summary>
    [Obsolete("使用 Begin/Draw/End 模式替代")]
    public void Apply(IFCSMaterial material, Texture2D source, RenderTarget2D destination = null, IntPtr vBufPtr = default, int stride = 0, int count = 3)
    {
        Begin(destination, material: material);
        Draw(source, vBufPtr, stride, count);
        End();
    }

    public void Dispose() => _driver.Dispose();
}