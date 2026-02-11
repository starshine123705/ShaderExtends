using ShaderExtends.Base;
using System;

namespace ShaderExtends.Interfaces
{
    public interface IRefCounted
    {
        void AddRef();
        void Release();
    }

    public interface IFCSEffect : IRefCounted, IDisposable
    {
        /// <summary>
        /// 着色器元数据
        /// </summary>
        FCSMetadata Metadata { get; }

        /// <summary>
        /// 顶点布局元数据（后端无关）
        /// </summary>
        ShaderVertexLayout VertexLayout { get; }
    }
}