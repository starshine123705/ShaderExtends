using Microsoft.Xna.Framework;
using ShaderExtends.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ShaderExtends.Base
{
    public static class VertexBufferRawWriter
    {
        // 浮点型
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteFloat(IntPtr ptr, float v) => *(float*)ptr = v;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteVector2(IntPtr ptr, Vector2 v) => *(Vector2*)ptr = v;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteVector3(IntPtr ptr, Vector3 v) => *(Vector3*)ptr = v;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteVector4(IntPtr ptr, Vector4 v) => *(Vector4*)ptr = v;

        // 整数型/颜色型 (HLSL 的 uint, int, Color 最终多为 4 字节)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteUInt(IntPtr ptr, uint v) => *(uint*)ptr = v;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteInt(IntPtr ptr, int v) => *(int*)ptr = v;
    }

    public unsafe delegate void SpriteVertexWriter(
        IntPtr destPtr,
        Rectangle source,
        Rectangle dest,
        int texWidth,
        int texHeight,
        float rotation,
        float depth,
        uint color,
        bool flipX,
        bool flipY,
        int viewportWidth,
        int viewportHeight);

    public static class SpriteVertexWriterFactory
    {
        /// <summary>
        /// 根据 FCS 元数据和顶点步长创建极致优化的动态顶点写入委托
        /// </summary>
        public static SpriteVertexWriter Create(List<InputElementMeta> elements, int stride)
        {
            var pDestPtr = Expression.Parameter(typeof(IntPtr), "destPtr");
            var pSource = Expression.Parameter(typeof(Rectangle), "source");
            var pDest = Expression.Parameter(typeof(Rectangle), "dest");
            var pTexW = Expression.Parameter(typeof(int), "texWidth");
            var pTexH = Expression.Parameter(typeof(int), "texHeight");
            var pRot = Expression.Parameter(typeof(float), "rotation");
            var pDepth = Expression.Parameter(typeof(float), "depth");
            var pColor = Expression.Parameter(typeof(uint), "color");
            var pFlipX = Expression.Parameter(typeof(bool), "flipX");
            var pFlipY = Expression.Parameter(typeof(bool), "flipY");
            var pViewportW = Expression.Parameter(typeof(int), "viewportWidth");
            var pViewportH = Expression.Parameter(typeof(int), "viewportHeight");

            var variables = new List<ParameterExpression>();
            var body = new List<Expression>();

            var uMin = Expression.Variable(typeof(float), "uMin");
            var vMin = Expression.Variable(typeof(float), "vMin");
            var uMax = Expression.Variable(typeof(float), "uMax");
            var vMax = Expression.Variable(typeof(float), "vMax");
            variables.AddRange(new[] { uMin, vMin, uMax, vMax });

            GenerateUVLogic(body, pSource, pTexW, pTexH, pFlipX, pFlipY, uMin, vMin, uMax, vMax);

            var c = Enumerable.Range(0, 8).Select(i => Expression.Variable(typeof(float), "c" + i)).ToArray();
            variables.AddRange(c);

            body.Add(Expression.IfThenElse(
                Expression.LessThan(Expression.Call(typeof(MathF).GetMethod("Abs", new[] { typeof(float) }), pRot), Expression.Constant(0.001f)),
                BuildSimpleRect(pDest, pViewportW, pViewportH, c),
                BuildRotatedRect(pDest, pRot, pViewportW, pViewportH, c)
            ));

            int[] pIdxMap = { 0, 2, 4, 4, 2, 6 };
            float[] uMap = { 0, 1, 0, 0, 1, 1 };
            float[] vMap = { 0, 0, 1, 1, 0, 1 };

            foreach (var el in elements)
            {
                for (int i = 0; i < 6; i++) // 处理 6 个顶点
                {
                    // 1. 计算绝对内存偏移
                    var offset = (long)(i * stride + el.AlignedByteOffset);
                    var addr = Expression.Add(Expression.Convert(pDestPtr, typeof(long)), Expression.Constant(offset));
                    var pTarget = Expression.Convert(addr, typeof(IntPtr));

                    // 2. 调用万能写入器
                    body.Add(BuildDynamicWrite(
                        pTarget, el, c, pIdxMap[i],
                        pDepth, pColor,
                        uMin, uMax, vMin, vMax,
                        uMap, vMap, i));
                }
            }

            return Expression.Lambda<SpriteVertexWriter>(
                Expression.Block(variables, body),
                pDestPtr, pSource, pDest, pTexW, pTexH, pRot, pDepth, pColor, pFlipX, pFlipY, pViewportW, pViewportH
            ).Compile();
        }

        /// <summary>
        /// 生成 UV 坐标计算逻辑，包含镜像翻转处理
        /// </summary>
        private static void GenerateUVLogic(List<Expression> body, ParameterExpression src, ParameterExpression tw, ParameterExpression th, ParameterExpression fx, ParameterExpression fy, ParameterExpression uMin, ParameterExpression vMin, ParameterExpression uMax, ParameterExpression vMax)
        {
            var invW = Expression.Divide(Expression.Constant(1.0f), Expression.Convert(tw, typeof(float)));
            var invH = Expression.Divide(Expression.Constant(1.0f), Expression.Convert(th, typeof(float)));

            var x = Expression.Convert(Expression.Field(src, "X"), typeof(float));
            var y = Expression.Convert(Expression.Field(src, "Y"), typeof(float));
            var w = Expression.Convert(Expression.Field(src, "Width"), typeof(float));
            var h = Expression.Convert(Expression.Field(src, "Height"), typeof(float));

            var curUMin = Expression.Multiply(x, invW);
            var curVMin = Expression.Multiply(y, invH);
            var curUMax = Expression.Multiply(Expression.Add(x, w), invW);
            var curVMax = Expression.Multiply(Expression.Add(y, h), invH);

            body.Add(Expression.Assign(uMin, Expression.Condition(fx, curUMax, curUMin)));
            body.Add(Expression.Assign(uMax, Expression.Condition(fx, curUMin, curUMax)));
            body.Add(Expression.Assign(vMin, Expression.Condition(fy, curVMax, curVMin)));
            body.Add(Expression.Assign(vMax, Expression.Condition(fy, curVMin, curVMax)));
        }

        /// <summary>
        /// 生成无旋转情况下的矩形顶点坐标计算块，映射到 NDC [-1, 1]
        /// </summary>
        private static Expression BuildSimpleRect(ParameterExpression dest, ParameterExpression viewportW, ParameterExpression viewportH, ParameterExpression[] c)
        {
            var l = Expression.Convert(Expression.Field(dest, "X"), typeof(float));
            var t = Expression.Convert(Expression.Field(dest, "Y"), typeof(float));
            var r = Expression.Convert(Expression.Add(Expression.Field(dest, "X"), Expression.Field(dest, "Width")), typeof(float));
            var b = Expression.Convert(Expression.Add(Expression.Field(dest, "Y"), Expression.Field(dest, "Height")), typeof(float));

            // 映射到 NDC 范围 [-1, 1]（DirectX 风格）
            var vw = Expression.Convert(viewportW, typeof(float));
            var vh = Expression.Convert(viewportH, typeof(float));
            var ndcL = Expression.Subtract(Expression.Multiply(Expression.Divide(l, vw), Expression.Constant(2f)), Expression.Constant(1f));
            var ndcT = Expression.Subtract(Expression.Constant(1f), Expression.Multiply(Expression.Divide(t, vh), Expression.Constant(2f)));
            var ndcR = Expression.Subtract(Expression.Multiply(Expression.Divide(r, vw), Expression.Constant(2f)), Expression.Constant(1f));
            var ndcB = Expression.Subtract(Expression.Constant(1f), Expression.Multiply(Expression.Divide(b, vh), Expression.Constant(2f)));

            return Expression.Block(
                Expression.Assign(c[0], ndcL), Expression.Assign(c[1], ndcT),
                Expression.Assign(c[2], ndcR), Expression.Assign(c[3], ndcT),
                Expression.Assign(c[4], ndcL), Expression.Assign(c[5], ndcB),
                Expression.Assign(c[6], ndcR), Expression.Assign(c[7], ndcB)
            );
        }

        /// <summary>
        /// 生成旋转情况下的矩形顶点坐标计算块，映射到 NDC [-1, 1]
        /// </summary>
        private static Expression BuildRotatedRect(ParameterExpression dest, ParameterExpression rot, ParameterExpression viewportW, ParameterExpression viewportH, ParameterExpression[] c)
        {
            var v = new
            {
                cos = Expression.Variable(typeof(float), "cos"),
                sin = Expression.Variable(typeof(float), "sin"),
                hw = Expression.Variable(typeof(float), "hw"),
                hh = Expression.Variable(typeof(float), "hh"),
                cx = Expression.Variable(typeof(float), "cx"),
                cy = Expression.Variable(typeof(float), "cy"),
                dx = Expression.Variable(typeof(float), "dx"),
                dy = Expression.Variable(typeof(float), "dy"),
                ux = Expression.Variable(typeof(float), "ux"),
                uy = Expression.Variable(typeof(float), "uy")
            };

            var exprs = new List<Expression>
            {
                Expression.Assign(v.cos, Expression.Call(typeof(MathF).GetMethod("Cos"), rot)),
                Expression.Assign(v.sin, Expression.Call(typeof(MathF).GetMethod("Sin"), rot)),
                Expression.Assign(v.hw, Expression.Multiply(Expression.Convert(Expression.Field(dest, "Width"), typeof(float)), Expression.Constant(0.5f))),
                Expression.Assign(v.hh, Expression.Multiply(Expression.Convert(Expression.Field(dest, "Height"), typeof(float)), Expression.Constant(0.5f))),
                Expression.Assign(v.cx, Expression.Add(Expression.Convert(Expression.Field(dest, "X"), typeof(float)), v.hw)),
                Expression.Assign(v.cy, Expression.Add(Expression.Convert(Expression.Field(dest, "Y"), typeof(float)), v.hh)),
                Expression.Assign(v.dx, Expression.Multiply(v.hw, v.cos)),
                Expression.Assign(v.dy, Expression.Multiply(v.hw, v.sin)),
                Expression.Assign(v.ux, Expression.Negate(Expression.Multiply(v.hh, v.sin))),
                Expression.Assign(v.uy, Expression.Multiply(v.hh, v.cos)),

                // 计算旋转后的像素坐标
                Expression.Assign(c[0], Expression.Subtract(Expression.Subtract(v.cx, v.dx), v.ux)),
                Expression.Assign(c[1], Expression.Subtract(Expression.Subtract(v.cy, v.dy), v.uy)),
                Expression.Assign(c[2], Expression.Subtract(Expression.Add(v.cx, v.dx), v.ux)),
                Expression.Assign(c[3], Expression.Subtract(Expression.Add(v.cy, v.dy), v.uy)),
                Expression.Assign(c[4], Expression.Add(Expression.Subtract(v.cx, v.dx), v.ux)),
                Expression.Assign(c[5], Expression.Add(Expression.Subtract(v.cy, v.dy), v.uy)),
                Expression.Assign(c[6], Expression.Add(Expression.Add(v.cx, v.dx), v.ux)),
                Expression.Assign(c[7], Expression.Add(Expression.Add(v.cy, v.dy), v.uy))
            };

            // 映射到 NDC
            var vw = Expression.Convert(viewportW, typeof(float));
            var vh = Expression.Convert(viewportH, typeof(float));
            for (int i = 0; i < 8; i += 2)
            {
                var pixelX = c[i];
                var pixelY = c[i + 1];
                var ndcX = Expression.Subtract(Expression.Multiply(Expression.Divide(pixelX, vw), Expression.Constant(2f)), Expression.Constant(1f));
                var ndcY = Expression.Subtract(Expression.Constant(1f), Expression.Multiply(Expression.Divide(pixelY, vh), Expression.Constant(2f)));
                exprs.Add(Expression.Assign(c[i], ndcX));
                exprs.Add(Expression.Assign(c[i + 1], ndcY));
            }

            return Expression.Block(new[] { v.cos, v.sin, v.hw, v.hh, v.cx, v.cy, v.dx, v.dy, v.ux, v.uy }, exprs);
        }

        private static Expression BuildDynamicWrite(
            Expression target,
            InputElementMeta el,
            Expression[] c, int pIdx, // 顶点坐标
            Expression pDepth, Expression pColor,
            Expression uMin, Expression uMax, Expression vMin, Expression vMax,
            float[] uMap, float[] vMap, int i)
        {
            var type = typeof(VertexBufferRawWriter);
            string semantic = el.SemanticName.ToUpper();
            string format = el.Format.ToLower();

            // --- 第一步：定义分量数据源 (默认值) ---
            Expression x = Expression.Constant(0f), y = Expression.Constant(0f),
                       z = Expression.Constant(0f), w = Expression.Constant(1.0f);

            // --- 第二步：根据语义(Semantic)分配数据 ---
            if (semantic.Contains("POSITION"))
            {
                x = c[pIdx]; y = c[pIdx + 1]; z = pDepth; // w 默认为 1.0f
            }
            else if (semantic.Contains("TEXCOORD"))
            {
                x = uMap[i] == 0 ? uMin : uMax;
                y = vMap[i] == 0 ? vMin : vMax;
            }
            else if (semantic.Contains("COLOR"))
            {
                // 如果 HLSL 是 uint 或 color 类型，直接写原始 uint 数据
                if (format == "color" || format == "uint" || format == "byte4")
                    return Expression.Call(type.GetMethod("WriteUInt"), target, pColor);

                // 如果 HLSL 是 float4，则需要位运算将 uint 颜色转为 4 个 float
                if (format == "float4")
                {
                    x = Expression.Divide(Expression.Convert(Expression.And(pColor, Expression.Constant(0xFFu)), typeof(float)), Expression.Constant(255f));
                    y = Expression.Divide(Expression.Convert(Expression.And(Expression.RightShift(pColor, Expression.Constant(8)), Expression.Constant(0xFFu)), typeof(float)), Expression.Constant(255f));
                    z = Expression.Divide(Expression.Convert(Expression.And(Expression.RightShift(pColor, Expression.Constant(16)), Expression.Constant(0xFFu)), typeof(float)), Expression.Constant(255f));
                    w = Expression.Divide(Expression.Convert(Expression.And(Expression.RightShift(pColor, Expression.Constant(24)), Expression.Constant(0xFFu)), typeof(float)), Expression.Constant(255f));
                }
            }

            // --- 第三步：根据 Format 决定最终写入动作 ---
            return format switch
            {
                "float" => Expression.Call(type.GetMethod("WriteFloat"), target, x),
                "float2" => Expression.Call(type.GetMethod("WriteVector2"), target,
                            Expression.New(typeof(Vector2).GetConstructor(new[] { typeof(float), typeof(float) }), x, y)),
                "float3" => Expression.Call(type.GetMethod("WriteVector3"), target,
                            Expression.New(typeof(Vector3).GetConstructor(new[] { typeof(float), typeof(float), typeof(float) }), x, y, z)),
                "float4" => Expression.Call(type.GetMethod("WriteVector4"), target,
                            Expression.New(typeof(Vector4).GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) }), x, y, z, w)),
                _ => throw new NotSupportedException($"不支持的 HLSL 类型: {format}")
            };
        }
    }
}