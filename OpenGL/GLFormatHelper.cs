using ShaderExtends.Base;
using OpenTK.Graphics.OpenGL4;

namespace ShaderExtends.OpenGL
{
    /// <summary>
    /// OpenGL 格式转换助手
    /// </summary>
    public static class GLFormatHelper
    {
        /// <summary>
        /// 获取 OpenGL 顶点属性类型
        /// </summary>
        public static VertexAttribPointerType ToGLType(this VertexElementFormat format) => format switch
        {
            VertexElementFormat.Float or VertexElementFormat.Float2
                or VertexElementFormat.Float3 or VertexElementFormat.Float4
                => VertexAttribPointerType.Float,

            VertexElementFormat.Color => VertexAttribPointerType.UnsignedByte,

            VertexElementFormat.UInt or VertexElementFormat.UInt2
                or VertexElementFormat.UInt3 or VertexElementFormat.UInt4
                => VertexAttribPointerType.UnsignedInt,

            VertexElementFormat.Int or VertexElementFormat.Int2
                or VertexElementFormat.Int3 or VertexElementFormat.Int4
                => VertexAttribPointerType.Int,

            VertexElementFormat.Short2 or VertexElementFormat.Short4
                => VertexAttribPointerType.Short,

            VertexElementFormat.HalfFloat2 or VertexElementFormat.HalfFloat4
                => VertexAttribPointerType.HalfFloat,

            _ => VertexAttribPointerType.Float
        };

        /// <summary>
        /// 是否需要归一化
        /// </summary>
        public static bool IsNormalized(this VertexElementFormat format) => format switch
        {
            VertexElementFormat.Color => true,
            VertexElementFormat.Short2 or VertexElementFormat.Short4 => true,
            _ => false
        };
    }
}