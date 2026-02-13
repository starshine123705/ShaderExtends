using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.D3D11;
using ShaderExtends.Interfaces;
using System;

namespace ShaderExtends.OpenGL
{
    public class GLFCSEffect : IFCSEffect
    {
        #region Properties

        public FCSMetadata Metadata { get; }
        public int GLProgram { get; private set; }
        public int GLCS { get; private set; }
        public ShaderVertexLayout VertexLayout { get; private set; }

        #endregion

        #region Reference Counting

        private int _refCount = 0;
        public void AddRef() => _refCount++;
        public void Release()
        {
            _refCount--;
            if (_refCount <= 0) Dispose();
        }

        #endregion

        #region Constructor

        public GLFCSEffect(FCSReader fcs)
        {
            Metadata = fcs.Metadata;

            if (!string.IsNullOrEmpty(fcs.GlslVS) && !string.IsNullOrEmpty(fcs.GlslPS))
            {
                GLProgram = CreateProgram(fcs.GlslVS, fcs.GlslPS);

                VertexLayout = BuildVertexLayoutFromMetadata();
            }

            if (!string.IsNullOrEmpty(fcs.GlslCS))
            {
                GLCS = CreateComputeProgram(fcs.GlslCS);
            }
        }

        #endregion

        #region Program Creation

        private int CreateProgram(string vsSource, string psSource)
        {
            int vs = CompileShader(ShaderType.VertexShader, vsSource);
            int ps = CompileShader(ShaderType.FragmentShader, psSource);
            int program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, ps);
            GL.LinkProgram(program);
            GL.DeleteShader(vs);
            GL.DeleteShader(ps);

            // 检查链接状态
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetProgramInfoLog(program);
                Console.WriteLine($"Program link failed: {log}");
            }

            return program;
        }

        private int CreateComputeProgram(string csSource)
        {
            int cs = CompileShader(ShaderType.ComputeShader, csSource);
            int program = GL.CreateProgram();
            GL.AttachShader(program, cs);
            GL.LinkProgram(program);
            GL.DeleteShader(cs);
            return program;
        }

        private int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                Console.WriteLine($"Shader compile failed: {log}");
            }

            return shader;
        }

        #endregion

        #region Vertex Layout Reflection

        /// <summary>
        /// 从 Metadata 构建顶点布局
        /// </summary>
        private ShaderVertexLayout BuildVertexLayoutFromMetadata()
        {
            if (Metadata.InputElements == null || Metadata.InputElements.Count == 0)
                return null;

            var layout = new ShaderVertexLayout();
            foreach (var e in Metadata.InputElements)
            {
                var format = D3D11FormatHelper.FromString(e.Format);
                layout.AddElement(new VertexElementInfo(
                    e.SemanticName,
                    e.SemanticIndex,
                    format,
                    e.AlignedByteOffset
                ));
            }
            return layout;
        }

        /// <summary>
        /// GL 类型转通用格式
        /// </summary>
        private static VertexElementFormat GLTypeToFormat(ActiveAttribType type) => type switch
        {
            ActiveAttribType.Float => VertexElementFormat.Float,
            ActiveAttribType.FloatVec2 => VertexElementFormat.Float2,
            ActiveAttribType.FloatVec3 => VertexElementFormat.Float3,
            ActiveAttribType.FloatVec4 => VertexElementFormat.Float4,
            ActiveAttribType.Int => VertexElementFormat.Int,
            ActiveAttribType.IntVec2 => VertexElementFormat.Int2,
            ActiveAttribType.IntVec3 => VertexElementFormat.Int3,
            ActiveAttribType.IntVec4 => VertexElementFormat.Int4,
            ActiveAttribType.UnsignedInt => VertexElementFormat.UInt,
            ActiveAttribType.UnsignedIntVec2 => VertexElementFormat.UInt2,
            ActiveAttribType.UnsignedIntVec3 => VertexElementFormat.UInt3,
            ActiveAttribType.UnsignedIntVec4 => VertexElementFormat.UInt4,
            _ => VertexElementFormat.Float4
        };

        /// <summary>
        /// 解析语义名（处理 HLSL 到 GLSL 的转换）
        /// 例如: "vs_POSITION0" -> ("POSITION", 0)
        /// </summary>
        private static void ParseSemanticName(string name, out string semanticName, out int semanticIndex)
        {
            semanticIndex = 0;

            // 移除 vs_/ps_ 前缀
            if (name.StartsWith("vs_")) name = name.Substring(3);
            if (name.StartsWith("ps_")) name = name.Substring(3);

            // 提取末尾数字
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;

            if (i < name.Length - 1)
            {
                semanticIndex = int.Parse(name.Substring(i + 1));
                semanticName = name.Substring(0, i + 1);
            }
            else
            {
                semanticName = name;
            }

            // 转换常见的 GLSL 名称到 HLSL 语义
            semanticName = semanticName.ToUpper() switch
            {
                "POSITION" or "POS" => "POSITION",
                "TEXCOORD" or "UV" => "TEXCOORD",
                "COLOR" or "COL" => "COLOR",
                "NORMAL" or "NORM" => "NORMAL",
                "TANGENT" or "TAN" => "TANGENT",
                _ => semanticName.ToUpper()
            };
        }

        #endregion

        #region Dispose

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (GLProgram != 0) GL.DeleteProgram(GLProgram);
            if (GLCS != 0) GL.DeleteProgram(GLCS);

            GLProgram = 0;
            GLCS = 0;

            GC.SuppressFinalize(this);
        }

        ~GLFCSEffect() => Dispose();

        #endregion
    }
}