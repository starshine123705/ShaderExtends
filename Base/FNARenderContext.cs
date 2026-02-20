using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.Core;
using System.Runtime.CompilerServices;
using ShaderExtends.D3D11;
using ShaderExtends.Interfaces;
using ShaderExtends.OpenGL;
using System;
using Buffer = System.Buffer;
using Color = Microsoft.Xna.Framework.Color;
using PrimitiveType = Microsoft.Xna.Framework.Graphics.PrimitiveType;

// 文件: Base/FNARenderContext.cs
// 说明: 顶层渲染上下文封装，按 SpriteBatch 风格提供 Begin/Draw/End 工作流，
//      根据当前 FNA 渲染后端选择合适的驱动（D3D11 或 OpenGL）。
//      负责将上层绘制调用转换为驱动可接受的参数，并管理临时顶点/索引池。
public class FNARenderContext : IDisposable
{
    private readonly IFNARenderDriver _driver;

    /// <summary>
    /// 获取内部使用的渲染驱动实例（D3D11 或 OpenGL 驱动）。
    /// 可用于直接访问底层驱动特有的方法或调试用途。
    /// </summary>
    /// <returns>当前使用的 <see cref="IFNARenderDriver"/> 实例。</returns>
    public IFNARenderDriver GetFNARenderDriver() => _driver;


    /// <summary>
    /// 创建渲染上下文并根据当前 FNA 后端初始化对应驱动（D3D11 或 OpenGL）。
    /// </summary>
    /// <param name="device">当前的 GraphicsDevice 实例。</param>
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
    /// 开始渲染批处理序列。设置目标渲染目标和渲染状态并进入批处理模式。
    /// 在调用 Begin 后应通过多次 Draw 提交绘制项，最后调用 End 完成并提交到 GPU。
    /// </summary>
    /// <param name="destination">渲染目标，传入 null 表示默认 backbuffer。</param>
    /// <param name="sortMode">SpriteSortMode，决定绘制排序策略（Immediate/Deferred/Texture 等）。</param>
    /// <param name="blendState">混合状态，可为 null 表示使用当前状态。</param>
    /// <param name="samplerState">采样器状态，可为 null 表示使用当前状态。</param>
    /// <param name="depthStencilState">深度模板状态，可为 null 表示使用当前状态。</param>
    /// <param name="rasterizerState">光栅化状态，可为 null 表示使用当前状态。</param>
    /// <param name="transformMatrix">可选变换矩阵，用于顶点变换（目前驱动视情况支持）。</param>
    /// <param name="material">要应用的 IFCSMaterial（着色器材质）。</param>
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
    /// <summary>
    /// 绘制整个纹理到指定位置（使用纹理宽高作为目标大小）。
    /// 会为该绘制项生成临时顶点缓冲并提交到驱动。
    /// </summary>
    /// <param name="texture">要绘制的纹理。</param>
    /// <param name="position">左上角绘制位置（像素坐标）。</param>
    /// <param name="color">顶点颜色。</param>
    public void Draw(Texture2D texture, Vector2 position, Color color)
    {
        var source = new Rectangle(0, 0, texture.Width, texture.Height);
        var dest = new Rectangle((int)position.X, (int)position.Y, texture.Width, texture.Height);
        nint vBufPtr = _driver.CreateVertexBuffer(source, dest, texture.Width, texture.Height, 0f, 0f, color.PackedValue, false, false);
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, _driver.CurrentMaterial.VertexStride, 4, 6, 0f, PrimitiveType.TriangleList);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, 0, 4, 6, 0f, PrimitiveType.TriangleList);
        }
    }

    /// <summary>
    /// 绘制整个纹理到指定目标矩形（缩放到目标大小）。
    /// </summary>
    /// <param name="texture">要绘制的纹理。</param>
    /// <param name="destinationRectangle">目标绘制矩形（像素坐标）。</param>
    /// <param name="color">顶点颜色。</param>
    public void Draw(Texture2D texture, Rectangle destinationRectangle, Color color)
    {
        var source = new Rectangle(0, 0, texture.Width, texture.Height);
        nint vBufPtr = _driver.CreateVertexBuffer(source, destinationRectangle, texture.Width, texture.Height, 0f, 0.2f, color.PackedValue, false, false);
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, _driver.CurrentMaterial.VertexStride, 4, 6, 0f, PrimitiveType.TriangleList);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, 0, 4, 6, 0f, PrimitiveType.TriangleList);
        }
    }

    /// <summary>
    /// 从纹理的 sourceRectangle 区域采样并绘制到目标矩形。
    /// </summary>
    /// <param name="texture">要绘制的纹理。</param>
    /// <param name="sourceRectangle">纹理内的源矩形。</param>
    /// <param name="destinationRectangle">目标绘制矩形。</param>
    /// <param name="color">顶点颜色。</param>
    public void Draw(Texture2D texture, Rectangle sourceRectangle, Rectangle destinationRectangle, Color color)
    {
        nint vBufPtr = _driver.CreateVertexBuffer(sourceRectangle, destinationRectangle, texture.Width, texture.Height, 0f, 0f, color.PackedValue, false, false);
        if (vBufPtr != IntPtr.Zero)
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, _driver.CurrentMaterial.VertexStride, 4, 6, 0f, PrimitiveType.TriangleList);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, 0, 4, 6, 0f, PrimitiveType.TriangleList);
        }
    }

    /// <summary>
    /// 绘制纹理的指定子区域，支持缩放、旋转和翻转。
    /// </summary>
    /// <param name="texture">要绘制的纹理。</param>
    /// <param name="position">绘制位置（左上角）。</param>
    /// <param name="sourceRectangle">纹理内的源矩形。</param>
    /// <param name="size">缩放系数。</param>
    /// <param name="rotation">旋转弧度。</param>
    /// <param name="color">顶点颜色。</param>
    /// <param name="effects">水平/垂直翻转标志。</param>
    /// <param name="depth">深度值（用于排序）。</param>
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
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, _driver.CurrentMaterial.VertexStride, 4, 6, depth, PrimitiveType.TriangleList);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, 0, 4, 6, depth, PrimitiveType.TriangleList);
        }
    }

    /// <summary>
    /// 将纹理的源区域绘制到目标矩形，支持缩放、旋转与翻转。
    /// </summary>
    /// <param name="texture">要绘制的纹理。</param>
    /// <param name="sourceRectangle">纹理内的源矩形。</param>
    /// <param name="destinationRectangle">目标绘制矩形。</param>
    /// <param name="size">缩放系数。</param>
    /// <param name="rotation">旋转弧度。</param>
    /// <param name="color">顶点颜色。</param>
    /// <param name="effects">翻转选项。</param>
    /// <param name="depth">深度值。</param>
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
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, _driver.CurrentMaterial.VertexStride, 4, 6, depth, PrimitiveType.TriangleList);
        }
        else
        {
            _driver.Draw(texture, vBufPtr, IntPtr.Zero, 0, 4, 6, depth, PrimitiveType.TriangleList);
        }
    }
    /// <summary>
    /// 添加绘制项
    /// </summary>
    public unsafe void Draw(
        Texture2D source,
        byte[] vBuffer = default,
        int stride = 0,
        int count = 3)
    {
        if (vBuffer == null || vBuffer.Length == 0)
        {
            _driver.Draw(source, IntPtr.Zero, IntPtr.Zero, stride, count, 0, PrimitiveType.TriangleList);
            return;
        }

        IntPtr nativePtr = GlobalVertexPool.RawPool.Rent(vBuffer.Length);
        fixed (byte* src = vBuffer)
        {
            Buffer.MemoryCopy(src, (void*)nativePtr, vBuffer.Length, vBuffer.Length);
        }

        _driver.Draw(source, nativePtr, IntPtr.Zero, stride, count, 6, PrimitiveType.TriangleList);
    }

    /// <summary>
    /// 使用指定纹理和顶点缓冲数据绘制用户自定义几何图元。
    /// </summary>
    /// <remarks>如果顶点缓冲为空或为 null，方法将使用无顶点模式进行绘制。请确保 `stride` 和 `vertexCount` 参数与提供的顶点数据的布局和长度一致，以避免渲染错误。</remarks>
    /// <param name="source">用于渲染图元的纹理，不能为空。</param>
    /// <param name="vBuffer">可选的顶点数据数组；若为 null 或为空，则使用内部顶点缓冲。</param>
    /// <param name="stride">每个顶点的字节大小，用于从缓冲中读取顶点数据。</param>
    /// <param name="vertexCount">要绘制的顶点数量，不得超过缓冲中可用的顶点数。</param>
    /// <param name="type">要渲染的图元类型（如三角形或线）。默认为 TriangleList。</param>
    public unsafe void DrawUserPrimitive(
        Texture2D source,
        byte[] vBuffer = default,
        int stride = 0,
        int vertexCount = 6,
        PrimitiveType type = PrimitiveType.TriangleList)
    {
        if (vBuffer == null || vBuffer.Length == 0)
        {
            _driver.Draw(source, IntPtr.Zero, IntPtr.Zero, stride, vertexCount, 0, type);
            return;
        }

        IntPtr nativePtr = GlobalVertexPool.RawPool.Rent(vBuffer.Length);
        fixed (byte* src = vBuffer)
        {
            Buffer.MemoryCopy(src, (void*)nativePtr, vBuffer.Length, vBuffer.Length);
        }

        _driver.Draw(source, nativePtr, IntPtr.Zero, stride, vertexCount, 0, type);
    }

    /// <summary>
    /// 使用指定的纹理、顶点缓冲和索引缓冲绘制用户自定义的索引图元。
    /// </summary>
    /// <remarks>如果顶点和索引缓冲都未提供或为空，则方法执行默认绘制，不使用自定义几何。`stride` 参数必须与顶点数据的布局匹配。此方法用于需要自定义几何的高级渲染场景。</remarks>
    /// <param name="source">用于渲染图元的纹理，不能为空。</param>
    /// <param name="vBuffer">可选的顶点数据字节数组；若为 null 或为空，则不使用自定义顶点数据进行绘制。</param>
    /// <param name="vIndexBuffer">可选的索引数据字节数组（ushort 格式）；若为 null 或为空，则不使用自定义索引。</param>
    /// <param name="stride">每个顶点元素的字节大小；如果提供了顶点数据，必须大于 0 且与布局匹配。</param>
    /// <param name="indexCount">用于绘制的索引数量。默认值为 3，适合绘制单个三角形。</param>
    /// <param name="type">要渲染的图元类型，默认为 TriangleList。</param>
    public unsafe void DrawUserIndexPrimitive(
        Texture2D source,
        byte[] vBuffer = default,
        byte[] vIndexBuffer = default,
        int stride = 0,
        int indexCount = 3,
        PrimitiveType type = PrimitiveType.TriangleList)
    {
        var count = vBuffer.Length / stride;
        if (vBuffer == null || vBuffer.Length == 0)
        {
            _driver.Draw(source, IntPtr.Zero, IntPtr.Zero, stride, count, 0, type);
            return;
        }

        IntPtr nativePtr = GlobalVertexPool.RawPool.Rent(vBuffer.Length);
        fixed (byte* src = vBuffer)
        {
            Buffer.MemoryCopy(src, (void*)nativePtr, vBuffer.Length, vBuffer.Length);
        }

        if (vIndexBuffer == null || vIndexBuffer.Length == 0)
        {
            _driver.Draw(source, nativePtr, IntPtr.Zero, stride, count, 0, type);
            return;
        }
        IntPtr nativeIndexPtr = GlobalVertexPool.IndexPool.Rent(vIndexBuffer.Length);
        fixed (byte* src = vIndexBuffer)
        {
            Buffer.MemoryCopy(src, (void*)nativeIndexPtr, vIndexBuffer.Length, vIndexBuffer.Length);
        }
        _driver.Draw(source, nativePtr, nativeIndexPtr, stride, count, indexCount, type);
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
    /// 兼容旧 API 的一次性绘制接口：内部会调用 Begin/Draw/End。
    /// 建议使用 Begin/Draw/End 的分步工作流以获得更好的批处理性能。
    /// </summary>
    /// <param name="material">要应用的材质。</param>
    /// <param name="source">纹理资源。</param>
    /// <param name="destination">可选目标渲染目标。</param>
    /// <param name="vBuffer">可选顶点数据。</param>
    /// <param name="stride">顶点步长（字节）。</param>
    /// <param name="count">顶点数量。</param>
    [Obsolete("使用 Begin/Draw/End 模式替代")]
    public void Apply(IFCSMaterial material, Texture2D source, RenderTarget2D destination = null, byte[] vBuffer = default, int stride = 0, int count = 6)
    {
        Begin(destination, material: material);
        Draw(source, vBuffer, stride, count);
        End();
    }

    /// <summary>
    /// 释放渲染上下文使用的内部资源并转发到底层驱动的 Dispose 实现。
    /// </summary>
    public void Dispose() => _driver.Dispose();
}