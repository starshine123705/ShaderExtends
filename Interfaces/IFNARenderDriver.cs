using Microsoft.Xna.Framework.Graphics;
using System;

namespace ShaderExtends.Interfaces
{
    /// <summary>
    /// 着色器渲染驱动程序，提供类似 SpriteBatch 的工作流程
    /// </summary>
    public interface IFNARenderDriver : IDisposable
    {
        /// <summary>
        /// 开始着色器渲染批处理，配置目标和渲染状态
        /// </summary>
        /// <param name="destination">目标渲染目标（null 为后台缓冲区）</param>
        /// <param name="sortMode">排序模式（影响渲染顺序）</param>
        /// <param name="blendState">混合状态（null 保持当前状态）</param>
        /// <param name="samplerState">采样器状态（null 保持当前状态）</param>
        /// <param name="depthStencilState">深度模板状态（null 保持当前状态）</param>
        /// <param name="rasterizerState">光栅化状态（null 保持当前状态）</param>
        /// <param name="transformMatrix">变换矩阵（用于坐标变换）</param>
        /// <param name="material">要应用的着色器材质</param>
        void Begin(
            RenderTarget2D destination = null,
            SpriteSortMode sortMode = SpriteSortMode.Deferred,
            BlendState blendState = null,
            SamplerState samplerState = null,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            Microsoft.Xna.Framework.Matrix? transformMatrix = null,
            IFCSMaterial material = null);

        /// <summary>
        /// 添加一个需要渲染的项目到批处理队列
        /// </summary>
        /// <param name="source">源纹理</param>
        /// <param name="vBufPtr">顶点缓冲区指针（可选）</param>
        /// <param name="stride">顶点步长（可选，默认为 0）</param>
        /// <param name="count">顶点/索引数量（默认为 3）</param>
        void Draw(
            Texture2D source,
            IntPtr vBufPtr = default,
            int stride = 0,
            int count = 3);

        /// <summary>
        /// 结束批处理并执行所有累积的渲染操作
        /// </summary>
        void End();

        /// <summary>
        /// 获取当前是否在批处理中
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// 获取当前的排序模式
        /// </summary>
        SpriteSortMode CurrentSortMode { get; }

        public nint CreateVertexBuffer(float[] data);
    }
}