using ShaderExtends.Base;
using Vortice.Direct3D;
using Vortice.Direct3D11.Shader;
using Vortice.DXGI;

namespace ShaderExtends.D3D11
{
    /// <summary>
    /// D3D11 格式转换助手
    /// </summary>
    public static class D3D11FormatHelper
    {
        /// <summary>
        /// 通用格式转 DXGI 格式
        /// </summary>
        public static Format ToD3D11Format(this VertexElementFormat format) => format switch
        {
            VertexElementFormat.Float => Format.R32_Float,
            VertexElementFormat.Float2 => Format.R32G32_Float,
            VertexElementFormat.Float3 => Format.R32G32B32_Float,
            VertexElementFormat.Float4 => Format.R32G32B32A32_Float,
            VertexElementFormat.Color => Format.R8G8B8A8_UNorm,
            VertexElementFormat.UInt => Format.R32_UInt,
            VertexElementFormat.UInt2 => Format.R32G32_UInt,
            VertexElementFormat.UInt3 => Format.R32G32B32_UInt,
            VertexElementFormat.UInt4 => Format.R32G32B32A32_UInt,
            VertexElementFormat.Int => Format.R32_SInt,
            VertexElementFormat.Int2 => Format.R32G32_SInt,
            VertexElementFormat.Int3 => Format.R32G32B32_SInt,
            VertexElementFormat.Int4 => Format.R32G32B32A32_SInt,
            VertexElementFormat.Short2 => Format.R16G16_SNorm,
            VertexElementFormat.Short4 => Format.R16G16B16A16_SNorm,
            VertexElementFormat.HalfFloat2 => Format.R16G16_Float,
            VertexElementFormat.HalfFloat4 => Format.R16G16B16A16_Float,
            _ => Format.Unknown
        };

        /// <summary>
        /// 从反射组件类型和掩码确定通用格式
        /// </summary>
        public static VertexElementFormat FromReflection(RegisterComponentType componentType, RegisterComponentMaskFlags mask)
        {
            int count = 0;
            if ((mask & RegisterComponentMaskFlags.ComponentX) != 0) count++;
            if ((mask & RegisterComponentMaskFlags.ComponentY) != 0) count++;
            if ((mask & RegisterComponentMaskFlags.ComponentZ) != 0) count++;
            if ((mask & RegisterComponentMaskFlags.ComponentW) != 0) count++;

            return componentType switch
            {
                RegisterComponentType.Float32 => count switch
                {
                    1 => VertexElementFormat.Float,
                    2 => VertexElementFormat.Float2,
                    3 => VertexElementFormat.Float3,
                    4 => VertexElementFormat.Float4,
                    _ => VertexElementFormat.Float4
                },
                RegisterComponentType.UInt32 => count switch
                {
                    1 => VertexElementFormat.UInt,
                    2 => VertexElementFormat.UInt2,
                    3 => VertexElementFormat.UInt3,
                    4 => VertexElementFormat.UInt4,
                    _ => VertexElementFormat.UInt4
                },
                RegisterComponentType.SInt32 => count switch
                {
                    1 => VertexElementFormat.Int,
                    2 => VertexElementFormat.Int2,
                    3 => VertexElementFormat.Int3,
                    4 => VertexElementFormat.Int4,
                    _ => VertexElementFormat.Int4
                },
                _ => VertexElementFormat.Float4
            };
        }

        /// <summary>
        /// 从格式字符串解析
        /// </summary>
        public static VertexElementFormat FromString(string fmt) => fmt.ToLower() switch
        {
            "float" => VertexElementFormat.Float,
            "float2" => VertexElementFormat.Float2,
            "float3" => VertexElementFormat.Float3,
            "float4" => VertexElementFormat.Float4,
            "color" => VertexElementFormat.Color,
            "uint" => VertexElementFormat.UInt,
            "uint2" => VertexElementFormat.UInt2,
            "uint3" => VertexElementFormat.UInt3,
            "uint4" => VertexElementFormat.UInt4,
            "int" => VertexElementFormat.Int,
            "int2" => VertexElementFormat.Int2,
            "int3" => VertexElementFormat.Int3,
            "int4" => VertexElementFormat.Int4,
            _ => VertexElementFormat.Float4
        };
    }
}