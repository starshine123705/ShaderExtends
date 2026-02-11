using ShaderExtends.Base;
using System;
using System.Collections.Generic;

namespace ShaderExtends.Interfaces
{
    public unsafe interface IFCSMaterial : IDisposable
    {
        /// <summary>
        /// 着色器参数
        /// </summary>
        Dictionary<string, FCSParameter> Parameters { get; }

        /// <summary>
        /// 获取顶点布局（后端无关）
        /// </summary>
        ShaderVertexLayout VertexLayout { get; }

        /// <summary>
        /// 阴影缓冲区
        /// </summary>
        IShadowBuffer Shadow { get; }

        /// <summary>
        /// 同步到 GPU
        /// </summary>
        void SyncToDevice(object deviceContext = null);

        /// <summary>
        /// 确保阴影缓冲区大小
        /// </summary>
        void EnsureShadow(int w, int h);

        /// <summary>
        /// 内部更新
        /// </summary>
        void InternalUpdate(int slot, int offset, void* src, int size);

        /// <summary>
        /// 应用材质
        /// </summary>
        void Apply();
    }
}