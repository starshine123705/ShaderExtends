using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using Vortice.Direct3D11;
using Vortice.DXGI;

public class D3D11FCSEffect : IFCSEffect
{
    public FCSMetadata Metadata { get; }
    public ID3D11VertexShader VS { get; private set; }
    public ID3D11PixelShader PS { get; private set; }
    public ID3D11InputLayout Layout { get; private set; }
    public ID3D11ComputeShader CS { get; private set; }

    public ID3D11Device d3D11Device { get; private set; }
    private int _refCount = 0;
    public void AddRef() => _refCount++;
    public void Release()
    {
        _refCount--;
        if (_refCount <= 0) Dispose(); 
    }
    public D3D11FCSEffect(ID3D11Device device, FCSReader fcs)
    {
        d3D11Device = device;
        Metadata = fcs.Metadata;

        if (fcs.DxbcVS is { Length: > 0 })
        {
            uint magic = BitConverter.ToUInt32(fcs.DxbcVS, 0);
            if (magic != 0x43425844)
            {
                Console.WriteLine("警告：这不是标准的 DXBC 格式，D3D11 无法加载！");
            }
            VS = device.CreateVertexShader(fcs.DxbcVS);

            if (Metadata.InputElements is { Count: > 0 })
            {
                var elements = new List<InputElementDescription>();
                foreach (var e in Metadata.InputElements)
                {
                    elements.Add(new InputElementDescription(
                        e.SemanticName, (uint)e.SemanticIndex, MapFormat(e.Format),
                        (uint)e.AlignedByteOffset, 0, InputClassification.PerVertexData, 0));
                }
                Layout = device.CreateInputLayout(elements.ToArray(), fcs.DxbcVS);
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

    private static Format MapFormat(string fmt) => fmt.ToLower() switch
    {
        "float3" => Format.R32G32B32_Float,
        "float2" => Format.R32G32_Float,
        "float4" => Format.R32G32B32A32_Float,
        "color" => Format.R8G8B8A8_UNorm,
        _ => Format.Unknown
    };

    public void Dispose()
    {
        VS?.Dispose();
        PS?.Dispose();
        Layout?.Dispose();
        CS?.Dispose();
    }
}