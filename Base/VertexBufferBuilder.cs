using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ShaderExtends.Base
{
    // 文件: Base/VertexBufferBuilder.cs
    // 说明: 提供若干便捷函数，用于构建常见的顶点布局（VertexPositionColorTexture），用于快速生成四边形
    //      的顶点数据。支持旋转、翻转、颜色与纹理坐标计算。返回值为生成的顶点数。
    public static class VertexBufferBuilder
    {
        public static int BuildVertices(Span<VertexPositionColorTexture> destPtr, in Rectangle source, in Rectangle dest, int texWidth, int texHeight, float rotation = 0f, float depth = 0f, uint color = 0xFFFFFFFF, bool filpX = false, bool filpY = false)
        {
            float u0 = source.X / texWidth;
            float v0 = source.Y / texHeight;
            float u1 = (source.X + source.Width) / texWidth;
            float v1 = (source.Y + source.Height) / texHeight;

            if (filpX)
            {
                (u0, u1) = (u1, u0);
            }

            if (filpY)
            {
                (v0, v1) = (v1, v0);
            }

            float centerX = dest.X + dest.Width * 0.5f;
            float centerY = dest.Y + dest.Height * 0.5f;

            float left = dest.Left;
            float top = dest.Top;
            float right = dest.Right;
            float bottom = dest.Bottom;

            Span<float> corners = stackalloc float[8];
            corners[0] = left;
            corners[1] = top;
            corners[2] = right;
            corners[3] = top;
            corners[4] = left;
            corners[5] = bottom;
            corners[6] = right;
            corners[7] = bottom;

            if (MathF.Abs(rotation) > 0.01f)
            {
                float cos = MathF.Cos(rotation);
                float sin = MathF.Sin(rotation);

                float halfWidth = dest.Width * 0.5f;
                float halfHeight = dest.Height * 0.5f;

                float rightX = halfWidth * cos;
                float rightY = halfWidth * sin;
                float bottomX = halfHeight * sin;
                float bottomY = halfHeight * cos;

                corners[0] = centerX - rightX - bottomX;
                corners[1] = centerY - rightY - bottomY;

                corners[2] = centerX + rightX - bottomX;
                corners[3] = centerY + rightY - bottomY;

                corners[4] = centerX - rightX + bottomX;
                corners[5] = centerY - rightY + bottomY;

                corners[6] = centerX + rightX + bottomX;
                corners[7] = centerY + rightY + bottomY;
            }

            // TriangleStrip order: TL, TR, BL, BR
            destPtr[0].Position.X = corners[0];
            destPtr[0].Position.Y = corners[1];
            destPtr[0].Position.Z = depth;
            destPtr[0].Color = color.ToUIntColor();
            destPtr[0].TextureCoordinate.X = u0;
            destPtr[0].TextureCoordinate.Y = v0;

            destPtr[1].Position.X = corners[2];
            destPtr[1].Position.Y = corners[3];
            destPtr[1].Position.Z = depth;
            destPtr[1].Color = color.ToUIntColor();
            destPtr[1].TextureCoordinate.X = u1;
            destPtr[1].TextureCoordinate.Y = v0;

            destPtr[2].Position.X = corners[4];
            destPtr[2].Position.Y = corners[5];
            destPtr[2].Position.Z = depth;
            destPtr[2].Color = color.ToUIntColor();
            destPtr[2].TextureCoordinate.X = u0;
            destPtr[2].TextureCoordinate.Y = v1;

            destPtr[3].Position.X = corners[6];
            destPtr[3].Position.Y = corners[7];
            destPtr[3].Position.Z = depth;
            destPtr[3].Color = color.ToUIntColor();
            destPtr[3].TextureCoordinate.X = u1;
            destPtr[3].TextureCoordinate.Y = v1;

            return 4;
        }

        public static int BuildVertices(Span<VertexPositionColorTexture> destPtr, in Rectangle source, in Vector2 dest, in float destWidth, in float destHeight, int texWidth, int texHeight, float rotation = 0f, float depth = 0f, uint color = 0xFFFFFFFF, bool filpX = false, bool filpY = false)
        {
            float u0 = source.X / texWidth;
            float v0 = source.Y / texHeight;
            float u1 = (source.X + source.Width) / texWidth;
            float v1 = (source.Y + source.Height) / texHeight;

            if (filpX)
            {
                (u0, u1) = (u1, u0);
            }

            if (filpY)
            {
                (v0, v1) = (v1, v0);
            }

            float centerX = dest.X + destWidth * 0.5f;
            float centerY = dest.Y + destHeight * 0.5f;

            float left = dest.X;
            float top = dest.Y;
            float right = dest.X + destWidth;
            float bottom = dest.Y + destHeight;

            Span<float> corners =
            [
                left,
                top,
                right,
                top,
                left,
                bottom,
                right,
                bottom,
            ];
            if (MathF.Abs(rotation) > 0.01f)
            {
                float cos = MathF.Cos(rotation);
                float sin = MathF.Sin(rotation);

                float halfWidth = destWidth * 0.5f;
                float halfHeight = destHeight * 0.5f;

                float rightX = halfWidth * cos;
                float rightY = halfWidth * sin;
                float bottomX = halfHeight * sin;
                float bottomY = halfHeight * cos;

                corners[0] = centerX - rightX - bottomX;
                corners[1] = centerY - rightY - bottomY;

                corners[2] = centerX + rightX - bottomX;
                corners[3] = centerY + rightY - bottomY;

                corners[4] = centerX - rightX + bottomX;
                corners[5] = centerY - rightY + bottomY;

                corners[6] = centerX + rightX + bottomX;
                corners[7] = centerY + rightY + bottomY;
            }

            // TriangleStrip order: TL, TR, BL, BR
            destPtr[0].Position.X = corners[0];
            destPtr[0].Position.Y = corners[1];
            destPtr[0].Position.Z = depth;
            destPtr[0].Color = color.ToUIntColor();
            destPtr[0].TextureCoordinate.X = u0;
            destPtr[0].TextureCoordinate.Y = v0;

            destPtr[1].Position.X = corners[2];
            destPtr[1].Position.Y = corners[3];
            destPtr[1].Position.Z = depth;
            destPtr[1].Color = color.ToUIntColor();
            destPtr[1].TextureCoordinate.X = u1;
            destPtr[1].TextureCoordinate.Y = v0;

            destPtr[2].Position.X = corners[4];
            destPtr[2].Position.Y = corners[5];
            destPtr[2].Position.Z = depth;
            destPtr[2].Color = color.ToUIntColor();
            destPtr[2].TextureCoordinate.X = u0;
            destPtr[2].TextureCoordinate.Y = v1;

            destPtr[3].Position.X = corners[6];
            destPtr[3].Position.Y = corners[7];
            destPtr[3].Position.Z = depth;
            destPtr[3].Color = color.ToUIntColor();
            destPtr[3].TextureCoordinate.X = u1;
            destPtr[3].TextureCoordinate.Y = v1;

            return 4;
        }

        public static int BuildVertices(Span<VertexPositionColorTexture> destPtr, in Rectangle texBounds, in Vector2 pos, float rotation = 0f, float depth = 0f, uint color = 0xFFFFFFFF, bool filpX = false, bool filpY = false)
        {
            float u0 = 0;
            float v0 = 0;
            float u1 = 1;
            float v1 = 1;

            if (filpX)
            {
                u0 = 1;
                u1 = 0;
            }

            if (filpY)
            {
                v0 = 1;
                v1 = 0;
            }

            float centerX = pos.X + texBounds.Width * 0.5f;
            float centerY = pos.Y + texBounds.Height * 0.5f;

            float left = pos.X;
            float top = pos.Y;
            float right = pos.X + texBounds.Width;
            float bottom = pos.Y + texBounds.Height;

            Span<float> corners =
            [
                left,
                top,
                right,
                top,
                left,
                bottom,
                right,
                bottom,
            ];
            if (MathF.Abs(rotation) > 0.01f)
            {
                float cos = MathF.Cos(rotation);
                float sin = MathF.Sin(rotation);

                float halfWidth = texBounds.Width * 0.5f;
                float halfHeight = texBounds.Height * 0.5f;

                float rightX = halfWidth * cos;
                float rightY = halfWidth * sin;
                float bottomX = halfHeight * sin;
                float bottomY = halfHeight * cos;

                corners[0] = centerX - rightX - bottomX;
                corners[1] = centerY - rightY - bottomY;

                corners[2] = centerX + rightX - bottomX;
                corners[3] = centerY + rightY - bottomY;

                corners[4] = centerX - rightX + bottomX;
                corners[5] = centerY - rightY + bottomY;

                corners[6] = centerX + rightX + bottomX;
                corners[7] = centerY + rightY + bottomY;
            }

            // TriangleStrip order: TL, TR, BL, BR
            destPtr[0].Position.X = corners[0];
            destPtr[0].Position.Y = corners[1];
            destPtr[0].Position.Z = depth;
            destPtr[0].Color = color.ToUIntColor();
            destPtr[0].TextureCoordinate.X = u0;
            destPtr[0].TextureCoordinate.Y = v0;

            destPtr[1].Position.X = corners[2];
            destPtr[1].Position.Y = corners[3];
            destPtr[1].Position.Z = depth;
            destPtr[1].Color = color.ToUIntColor();
            destPtr[1].TextureCoordinate.X = u1;
            destPtr[1].TextureCoordinate.Y = v0;

            destPtr[2].Position.X = corners[4];
            destPtr[2].Position.Y = corners[5];
            destPtr[2].Position.Z = depth;
            destPtr[2].Color = color.ToUIntColor();
            destPtr[2].TextureCoordinate.X = u0;
            destPtr[2].TextureCoordinate.Y = v1;

            destPtr[3].Position.X = corners[6];
            destPtr[3].Position.Y = corners[7];
            destPtr[3].Position.Z = depth;
            destPtr[3].Color = color.ToUIntColor();
            destPtr[3].TextureCoordinate.X = u1;
            destPtr[3].TextureCoordinate.Y = v1;

            return 4;
        }

        public static Color ToUIntColor(this uint color)
        {
            Color result = new();
            result.PackedValue = color;
            return result;
        }
    }
}

