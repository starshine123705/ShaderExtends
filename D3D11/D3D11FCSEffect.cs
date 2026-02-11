using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Shader;
using Vortice.DXGI;

namespace ShaderExtends.D3D11
{
    public class D3D11FCSEffect : IFCSEffect
    {
        #region Properties

        public FCSMetadata Metadata { get; }
        public ID3D11VertexShader VS { get; private set; }
        public ID3D11PixelShader PS { get; private set; }
        public ID3D11InputLayout Layout { get; private set; }
        public ID3D11ComputeShader CS { get; private set; }
        public byte[] VSBytecode { get; private set; }
        public ShaderVertexLayout VertexLayout { get; private set; }
        public ID3D11Device D3D11Device { get; private set; }

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

        public D3D11FCSEffect(ID3D11Device device, FCSReader fcs)
        {
            D3D11Device = device;
            Metadata = fcs.Metadata;
            VSBytecode = fcs.DxbcVS;

            if (fcs.DxbcVS is { Length: > 0 })
            {
                ValidateDxbcMagic(fcs.DxbcVS);
                VS = device.CreateVertexShader(fcs.DxbcVS);

                // 通过反射构建顶点布局
                VertexLayout = BuildVertexLayoutFromReflection(fcs.DxbcVS);

                // 创建 InputLayout
                if (VertexLayout != null && VertexLayout.Elements.Count > 0)
                {
                    Layout = device.CreateInputLayout(
                        BuildInputElements(VertexLayout),
                        fcs.DxbcVS);
                }
            }

            if (fcs.DxbcPS is { Length: > 0 })
            {
                PS = device.CreatePixelShader(fcs.DxbcPS);
            }

            if (fcs.DxbcCS is { Length: > 0 })
            {
                CS = device.CreateComputeShader(fcs.DxbcCS);
            }
        }

        #endregion

        #region Layout Building

        private static void ValidateDxbcMagic(byte[] bytecode)
        {
            if (bytecode.Length < 4) return;
            uint magic = BitConverter.ToUInt32(bytecode, 0);
            if (magic != 0x43425844)
            {
                Console.WriteLine("警告：这不是标准的 DXBC 格式！");
            }
        }

        private ShaderVertexLayout BuildVertexLayoutFromReflection(byte[] vsBytecode)
        {
            try
            {
                using var reflection = D3D11Device.Reflect<ID3D11ShaderReflection>(vsBytecode);
                var shaderDesc = reflection.Description;
                var layout = new ShaderVertexLayout();

                for (int i = 0; i < shaderDesc.InputParameters; i++)
                {
                    var paramDesc = reflection.GetInputParameterDescription(i);
                    var format = D3D11FormatHelper.FromReflection(paramDesc.ComponentType, paramDesc.Mask);

                    layout.AddElement(paramDesc.SemanticName, paramDesc.SemanticIndex, format);
                }

#if DEBUG
                layout.DebugPrint();
#endif
                return layout;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"着色器反射失败: {ex.Message}");
                return BuildVertexLayoutFromMetadata();
            }
        }

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
        /// 从通用布局构建 D3D11 InputElementDescription
        /// </summary>
        private static InputElementDescription[] BuildInputElements(ShaderVertexLayout layout)
        {
            var result = new InputElementDescription[layout.Elements.Count];
            for (int i = 0; i < layout.Elements.Count; i++)
            {
                var e = layout.Elements[i];
                result[i] = new InputElementDescription(
                    e.SemanticName,
                    (uint)e.SemanticIndex,
                    e.Format.ToD3D11Format(),
                    (uint)e.Offset,
                    0,
                    InputClassification.PerVertexData,
                    0
                );
            }
            return result;
        }

        #endregion

        #region Dispose

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            VS?.Dispose();
            PS?.Dispose();
            Layout?.Dispose();
            CS?.Dispose();

            GC.SuppressFinalize(this);
        }

        ~D3D11FCSEffect() => Dispose();

        #endregion
    }
}