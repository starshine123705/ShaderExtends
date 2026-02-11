namespace ShaderExtends.Base
{
    /// <summary>
    /// 后端无关的顶点元素格式
    /// </summary>
    public enum VertexElementFormat
    {
        Float,
        Float2,
        Float3,
        Float4,
        Color,      // R8G8B8A8
        UInt,
        UInt2,
        UInt3,
        UInt4,
        Int,
        Int2,
        Int3,
        Int4,
        Short2,
        Short4,
        HalfFloat2,
        HalfFloat4,
    }

    public static class VertexElementFormatExtensions
    {
        /// <summary>
        /// 获取格式的字节大小
        /// </summary>
        public static int GetSize(this VertexElementFormat format) => format switch
        {
            VertexElementFormat.Float => 4,
            VertexElementFormat.Float2 => 8,
            VertexElementFormat.Float3 => 12,
            VertexElementFormat.Float4 => 16,
            VertexElementFormat.Color => 4,
            VertexElementFormat.UInt => 4,
            VertexElementFormat.UInt2 => 8,
            VertexElementFormat.UInt3 => 12,
            VertexElementFormat.UInt4 => 16,
            VertexElementFormat.Int => 4,
            VertexElementFormat.Int2 => 8,
            VertexElementFormat.Int3 => 12,
            VertexElementFormat.Int4 => 16,
            VertexElementFormat.Short2 => 4,
            VertexElementFormat.Short4 => 8,
            VertexElementFormat.HalfFloat2 => 4,
            VertexElementFormat.HalfFloat4 => 8,
            _ => 0
        };

        /// <summary>
        /// 获取组件数量
        /// </summary>
        public static int GetComponentCount(this VertexElementFormat format) => format switch
        {
            VertexElementFormat.Float or VertexElementFormat.UInt or VertexElementFormat.Int => 1,
            VertexElementFormat.Float2 or VertexElementFormat.UInt2 or VertexElementFormat.Int2
                or VertexElementFormat.Short2 or VertexElementFormat.HalfFloat2 => 2,
            VertexElementFormat.Float3 or VertexElementFormat.UInt3 or VertexElementFormat.Int3 => 3,
            VertexElementFormat.Float4 or VertexElementFormat.UInt4 or VertexElementFormat.Int4
                or VertexElementFormat.Color or VertexElementFormat.Short4 or VertexElementFormat.HalfFloat4 => 4,
            _ => 0
        };
    }
}