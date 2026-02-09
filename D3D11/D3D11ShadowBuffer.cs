using ShaderExtends.Interfaces;
using System;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ShaderExtends.D3D11
{
    public class D3D11ShadowBuffer : IShadowBuffer
    {
        public ID3D11Texture2D Texture;
        public ID3D11UnorderedAccessView UAV;
        public ID3D11ShaderResourceView SRV;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public D3D11ShadowBuffer(ID3D11Device device, int width, int height)
        {
            this.Width = width;
            this.Height = height;

            var desc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None
            };

            Texture = device.CreateTexture2D(desc);
            UAV = device.CreateUnorderedAccessView(Texture);
            SRV = device.CreateShaderResourceView(Texture);
        }

        public void Dispose()
        {
            UAV?.Dispose();
            SRV?.Dispose();
            Texture?.Dispose();
        }
    }
}