using System;
using System.Collections.Generic;

namespace ShaderExtends.Base
{
    /// <summary>
    /// 顶点元素描述（后端无关）
    /// </summary>
    public struct VertexElementInfo
    {
        public string SemanticName;
        public int SemanticIndex;
        public VertexElementFormat Format;
        public int Offset;
        public int Size;

        public VertexElementInfo(string name, int index, VertexElementFormat format, int offset)
        {
            SemanticName = name;
            SemanticIndex = index;
            Format = format;
            Offset = offset;
            Size = format.GetSize();
        }

        public override string ToString()
            => $"{SemanticName}{SemanticIndex} @ offset {Offset} ({Format}, {Size} bytes)";
    }

    /// <summary>
    /// 着色器顶点布局（后端无关）
    /// </summary>
    public class ShaderVertexLayout
    {
        /// <summary>
        /// 顶点元素列表
        /// </summary>
        public List<VertexElementInfo> Elements { get; } = new();

        /// <summary>
        /// 顶点总大小（字节）
        /// </summary>
        public int Stride { get; private set; }

        /// <summary>
        /// 根据语义名和索引查找元素
        /// </summary>
        public VertexElementInfo? GetElement(string semanticName, int semanticIndex = 0)
        {
            for (int i = 0; i < Elements.Count; i++)
            {
                var e = Elements[i];
                if (e.SemanticName.Equals(semanticName, StringComparison.OrdinalIgnoreCase)
                    && e.SemanticIndex == semanticIndex)
                {
                    return e;
                }
            }
            return null;
        }

        /// <summary>
        /// 检查是否包含指定语义
        /// </summary>
        public bool HasSemantic(string semanticName, int semanticIndex = 0)
            => GetElement(semanticName, semanticIndex) != null;

        /// <summary>
        /// 添加元素
        /// </summary>
        public void AddElement(VertexElementInfo element)
        {
            Elements.Add(element);
            Stride = Math.Max(Stride, element.Offset + element.Size);
        }

        /// <summary>
        /// 添加元素（自动计算偏移）
        /// </summary>
        public void AddElement(string semanticName, int semanticIndex, VertexElementFormat format)
        {
            var element = new VertexElementInfo(semanticName, semanticIndex, format, Stride);
            Elements.Add(element);
            Stride += element.Size;
        }

        /// <summary>
        /// 打印布局信息（调试用）
        /// </summary>
        public void DebugPrint()
        {
            Console.WriteLine($"=== ShaderVertexLayout (Stride: {Stride} bytes) ===");
            foreach (var e in Elements)
            {
                Console.WriteLine($"  {e}");
            }
            Console.WriteLine("===================================================");
        }
    }
}