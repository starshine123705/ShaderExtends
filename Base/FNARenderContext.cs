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
        //Span<VertexPositionColorTexture> vertices = stackalloc VertexPositionColorTexture[6];
        //int count = VertexBufferBuilder.BuildVertices(vertices, texture.Bounds, position);
         
    }

    public void Draw(Texture2D texture, Rectangle destinationRectangle, Color color)
    {
        
    }

    public void Draw(Texture2D texture, Rectangle sourceRectangle, Rectangle destinationRectangle, Color color)
    {
        
    }

    public void Draw(Texture2D texture, Vector2 position, Rectangle sourceRectangle, float size, float rotation, Color color, SpriteEffects effects, float depth)
    {

    }
    public void Draw(Texture2D texture, Rectangle sourceRectangle, Rectangle destinationRectangle, float size, float rotation, Color color, SpriteEffects effects, float depth)
    {

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