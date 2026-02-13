using Microsoft.Xna.Framework.Graphics;
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
        public SpriteVertexWriter VertexWriter { get; }
        
        /// <summary>
        /// 顶点布局字节大小
        /// </summary>
        public int VertexStride { get; }

        /// <summary>
        /// 阴影缓冲区
        /// </summary>
        IShadowBuffer Shadow { get; }

        public Texture[] SourceTexture { get; }

        /// <summary>
        /// 同步到 GPU
        /// </summary>
        void SyncToDevice();

        /// <summary>
        /// 确保阴影缓冲区大小
        /// </summary>
        void EnsureShadow(int w, int h, int d = -1);

        /// <summary>
        /// 内部更新
        /// </summary>
        void InternalUpdate(int slot, int offset, void* src, int size);

        /// <summary>
        /// 应用材质
        /// </summary>
        void Apply(IFNARenderDriver driver);


        public static int GetFormatSize(string format) => format.ToLower() switch
        {
            "float4" => 16,
            "float3" => 12,
            "float2" => 8,
            "float" => 4,
            "uint" => 4,
            "int" => 4,
            _ => throw new NotSupportedException($"未知的顶点格式类型: {format}")
        };
    }
}