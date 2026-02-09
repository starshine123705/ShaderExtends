using Microsoft.Xna.Framework.Graphics;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Blend = Vortice.Direct3D11.Blend;
using TextureAddressMode = Vortice.Direct3D11.TextureAddressMode;
using Viewport = Vortice.Mathematics.Viewport;

namespace ShaderExtends.D3D11
{
    public unsafe class FNAD3D11Driver : IDisposable, IFNARenderDriver
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ID3D11Device _d3dDevice;
        private readonly ID3D11DeviceContext _d3dContext;
        private readonly ID3D11SamplerState _samplerState;
        private readonly ID3D11BlendState _blendState;
        public FNAD3D11Driver(GraphicsDevice device, FNA3D_SysRendererEXT backend)
        {
            _graphicsDevice = device;
            _d3dDevice = MarshallingHelpers.FromPointer<ID3D11Device>(backend.d3d11_device);
            _d3dContext = MarshallingHelpers.FromPointer<ID3D11DeviceContext>(backend.d3d11_context);

            _samplerState = _d3dDevice.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });

            var blendDesc = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };
            blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = false,
                SourceBlend = Blend.One,
                DestinationBlend = Blend.Zero,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.Zero,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };

            _blendState = _d3dDevice.CreateBlendState(blendDesc);
        }
        public void Apply(IFCSMaterial material, Texture2D source, RenderTarget2D destination = null, IntPtr vBufPtr = default, int stride = 0, int count = 3)
        {
            if (!(material is D3D11FCSMaterial d3dMat)) return;

            d3dMat.SyncToDevice(_d3dContext);

            IntPtr sourceSRVPtr = D3D11TextureHelper.GetD3D11SRV_Method2(source);
            IntPtr destResourcePtr = (destination != null) ? D3D11TextureHelper.GetD3D11ResourceHandle(destination) : IntPtr.Zero;

            ID3D11VertexShader oldVS = _d3dContext.VSGetShader();
            ID3D11PixelShader oldPS = _d3dContext.PSGetShader();
            ID3D11ComputeShader oldCS = _d3dContext.CSGetShader();
            ID3D11InputLayout oldLayout = _d3dContext.IAGetInputLayout();
            PrimitiveTopology oldTopology = _d3dContext.IAGetPrimitiveTopology();

            ID3D11Buffer oldIndexBuffer;
            Format oldIndexFormat;
            uint oldIndexOffset;
            _d3dContext.IAGetIndexBuffer(out oldIndexBuffer, out oldIndexFormat, out oldIndexOffset);

            ID3D11Buffer[] oldVertexBuffers = new ID3D11Buffer[1];
            uint[] oldStrides = new uint[1];
            uint[] oldOffsets = new uint[1];
            _d3dContext.IAGetVertexBuffers(0, 1, oldVertexBuffers, oldStrides, oldOffsets);

            ID3D11BlendState oldBlend;
            float[] oldFactors = new float[4];
            uint oldSampleMask;
            fixed (float* pFactors = oldFactors) _d3dContext.OMGetBlendState(out oldBlend, pFactors, out oldSampleMask);

            ID3D11DepthStencilState oldDepth;
            uint oldStencilRef;
            _d3dContext.OMGetDepthStencilState(out oldDepth, out oldStencilRef);
            ID3D11RasterizerState oldRaster = _d3dContext.RSGetState();

            ID3D11Buffer[] oldVSBuffers = new ID3D11Buffer[8];
            ID3D11Buffer[] oldPSBuffers = new ID3D11Buffer[8];
            _d3dContext.VSGetConstantBuffers(0, 8, oldVSBuffers);
            _d3dContext.PSGetConstantBuffers(0, 8, oldPSBuffers);

            ID3D11ShaderResourceView[] oldSRVs = new ID3D11ShaderResourceView[1];
            _d3dContext.PSGetShaderResources(0, 1, oldSRVs);
            ID3D11SamplerState[] oldSamps = new ID3D11SamplerState[1];
            _d3dContext.PSGetSamplers(0, 1, oldSamps);

            Vortice.Mathematics.Viewport[] oldViewports = new Vortice.Mathematics.Viewport[16];
            uint viewportCount = 16;
            _d3dContext.RSGetViewports(ref viewportCount, oldViewports);
            if (viewportCount < 16) Array.Resize(ref oldViewports, (int)viewportCount);

            try
            {
                // 预解除 Slot 0 读写冲突
                _d3dContext.PSSetShaderResource(0, null);
                _d3dContext.Flush();

                // --- 2. Compute Shader 阶段 ---
                if (d3dMat.Effect.CS != null)
                {
                    d3dMat.EnsureShadow(source.Width, source.Height);
                    var shadow = (D3D11ShadowBuffer)d3dMat.Shadow;
                    _d3dContext.CSSetShader(d3dMat.Effect.CS);

                    for (int i = 0; i < 8; i++)
                    {
                        var buf = d3dMat.GetBuffer(i);
                        if (buf != null) _d3dContext.CSSetConstantBuffer((uint)i, buf);
                    }

                    var srcSRV = MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(sourceSRVPtr);
                    _d3dContext.CSSetShaderResource(0, srcSRV);
                    _d3dContext.CSSetUnorderedAccessView(0, shadow.UAV);
                    _d3dContext.Dispatch((uint)d3dMat.GroupsX, (uint)d3dMat.GroupsY, 1);

                    _d3dContext.CSSetShader(null);
                    _d3dContext.CSSetUnorderedAccessView(0, null);
                    _d3dContext.CSSetShaderResource(0, null);

                    if (destResourcePtr != IntPtr.Zero)
                    {
                        var d3dDest = MarshallingHelpers.FromPointer<ID3D11Resource>(destResourcePtr);
                        _d3dContext.CopyResource(d3dDest, shadow.Texture);
                    }
                }

                if (d3dMat.Effect.VS != null && d3dMat.Effect.PS != null)
                {
                    _d3dContext.VSSetShader(d3dMat.Effect.VS);
                    _d3dContext.PSSetShader(d3dMat.Effect.PS);
                    _d3dContext.IASetInputLayout(d3dMat.Effect.Layout.Tag == null ? null : d3dMat.Effect.Layout);
                    _d3dContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);

                    if (vBufPtr != IntPtr.Zero)
                    {
                        var vBuf = MarshallingHelpers.FromPointer<ID3D11Buffer>(vBufPtr);
                        _d3dContext.IASetVertexBuffer(0, vBuf, (uint)stride, 0);
                    }

                    for (int i = 0; i < 8; i++)
                    {
                        var buf = d3dMat.GetBuffer(i);
                        if (buf != null)
                        {
                            _d3dContext.VSSetConstantBuffer((uint)i, buf);
                            _d3dContext.PSSetConstantBuffer((uint)i, buf);
                        }
                    }

                    var inputSRV = (d3dMat.Effect.CS != null)
                        ? ((D3D11ShadowBuffer)d3dMat.Shadow).SRV
                        : MarshallingHelpers.FromPointer<ID3D11ShaderResourceView>(sourceSRVPtr);

                    _d3dContext.PSSetShaderResource(0, inputSRV);
                    _d3dContext.PSSetSampler(0, _samplerState);
                    _d3dContext.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, source.Width, source.Height));

                    _d3dContext.OMSetDepthStencilState(null, 0);
                    _d3dContext.OMSetBlendState(_blendState, new Color4(0, 0, 0, 0), 0xffffffff);
                    _d3dContext.RSSetState(null);

                    _d3dContext.Draw((uint)count, 0);
                }
            }
            finally
            {
                _d3dContext.PSSetShaderResource(0, null);
                _d3dContext.IASetVertexBuffers(0, 1, new ID3D11Buffer[] { null }, new uint[] { 0 }, new uint[] { 0 });
                _d3dContext.VSSetConstantBuffers(0, 8, new ID3D11Buffer[8]);
                _d3dContext.PSSetConstantBuffers(0, 8, new ID3D11Buffer[8]);
                _d3dContext.Flush();

                _d3dContext.VSSetShader(oldVS);
                _d3dContext.PSSetShader(oldPS);
                _d3dContext.CSSetShader(oldCS);
                _d3dContext.IASetInputLayout(oldLayout);
                _d3dContext.IASetPrimitiveTopology(oldTopology);
                _d3dContext.IASetIndexBuffer(oldIndexBuffer, oldIndexFormat, oldIndexOffset);
                _d3dContext.IASetVertexBuffers(0, 1, oldVertexBuffers, oldStrides, oldOffsets);

                _d3dContext.VSSetConstantBuffers(0, 8, oldVSBuffers);
                _d3dContext.PSSetConstantBuffers(0, 8, oldPSBuffers);
                _d3dContext.PSSetShaderResource(0, oldSRVs[0]);
                _d3dContext.PSSetSampler(0, oldSamps[0]);

                if (oldViewports != null && oldViewports.Length > 0)
                {
                    _d3dContext.RSSetViewports(oldViewports);
                }

                fixed (float* pRestore = oldFactors) _d3dContext.OMSetBlendState(oldBlend, pRestore, oldSampleMask);
                _d3dContext.OMSetDepthStencilState(oldDepth, oldStencilRef);
                _d3dContext.RSSetState(oldRaster);

                _d3dContext.Flush();

                oldVS?.Dispose(); oldPS?.Dispose(); oldCS?.Dispose(); oldLayout?.Dispose();
                oldIndexBuffer?.Dispose(); oldVertexBuffers[0]?.Dispose();
                oldBlend?.Dispose(); oldDepth?.Dispose(); oldRaster?.Dispose();
                oldSRVs[0]?.Dispose(); oldSamps[0]?.Dispose();
                foreach (var b in oldVSBuffers) b?.Dispose();
                foreach (var b in oldPSBuffers) b?.Dispose();
            }
        }

        public void Dispose()
        {
            _samplerState?.Dispose();
        }
    }
}