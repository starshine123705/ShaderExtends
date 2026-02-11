using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using Vortice.Direct3D11;

namespace ShaderExtends.D3D11
{
    public unsafe class D3D11FCSMaterial : IFCSMaterial
    {
        public D3D11FCSEffect Effect { get; }
        public Dictionary<string, FCSParameter> Parameters { get; } = [];

        /// <summary>
        /// 从 Effect 获取顶点布局
        /// </summary>
        public ShaderVertexLayout VertexLayout => Effect.VertexLayout;

        private readonly Dictionary<int, byte[]> _cpuBuffers = new();
        private readonly Dictionary<int, ID3D11Buffer> _gpuBuffers = new();
        private readonly HashSet<int> _dirtySlots = new();
        private readonly Action _onDispose;

        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;

        public IShadowBuffer Shadow { get; private set; }
        public int GroupsX { get; private set; }
        public int GroupsY { get; private set; }

        public D3D11FCSMaterial(ID3D11Device device, ID3D11DeviceContext context, D3D11FCSEffect effect, Action onDispose)
        {
            _device = device;
            _context = context;
            Effect = effect;
            _onDispose = onDispose;

            foreach (var cb in effect.Metadata.Buffers)
            {
                _cpuBuffers[cb.Slot] = new byte[cb.TotalSize];
                _gpuBuffers[cb.Slot] = device.CreateBuffer(new BufferDescription
                {
                    ByteWidth = (uint)cb.TotalSize,
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ConstantBuffer
                });

                foreach (var varMeta in cb.Variables)
                {
                    Parameters[varMeta.Key] = new FCSParameter(
                        varMeta.Key, varMeta.Value.Offset, varMeta.Value.Size, cb.Slot, this);
                }
            }
        }

        public void UpdateBuffer(int slot, byte[] data)
        {
            _cpuBuffers[slot] = data;
            _dirtySlots.Add(slot);
        }

        public void SyncToDevice(object deviceContext = null)
        {
            var context = deviceContext as ID3D11DeviceContext ?? _context;
            foreach (var slot in _dirtySlots)
            {
                fixed (byte* pData = _cpuBuffers[slot])
                {
                    context.UpdateSubresource(new ReadOnlySpan<byte>(pData, _cpuBuffers[slot].Length), _gpuBuffers[slot]);
                }
            }
            _dirtySlots.Clear();
        }

        public void EnsureShadow(int w, int h)
        {
            if (Shadow != null && Shadow.Width == w && Shadow.Height == h) return;
            Shadow?.Dispose();
            Shadow = new D3D11ShadowBuffer(_device, w, h);
            GroupsX = (w + 15) / 16;
            GroupsY = (h + 15) / 16;
        }

        public ID3D11Buffer GetBuffer(int slot) => _gpuBuffers.TryGetValue(slot, out var b) ? b : null;

        public void InternalUpdate(int slot, int offset, void* src, int size)
        {
            fixed (byte* pBuf = _cpuBuffers[slot])
                Buffer.MemoryCopy(src, pBuf + offset, size, size);
            _dirtySlots.Add(slot);
        }

        public void Apply()
        {
            var context = _context;

            SyncToDevice(context);

            if (Effect.VS != null)
                context.VSSetShader(Effect.VS);

            if (Effect.PS != null)
                context.PSSetShader(Effect.PS);

            if (Effect.Layout != null)
                context.IASetInputLayout(Effect.Layout);

            for (int i = 0; i < 8; i++)
            {
                var buf = GetBuffer(i);
                if (buf != null)
                {
                    context.VSSetConstantBuffer((uint)i, buf);
                    context.PSSetConstantBuffer((uint)i, buf);
                }
            }

            if (Effect.CS != null)
            {
                context.CSSetShader(Effect.CS);
                for (int i = 0; i < 8; i++)
                {
                    var buf = GetBuffer(i);
                    if (buf != null)
                    {
                        context.CSSetConstantBuffer((uint)i, buf);
                    }
                }
            }
        }

        public void Dispose()
        {
            foreach (var b in _gpuBuffers.Values) b.Dispose();
            Shadow?.Dispose();
            Effect.Release();
            _onDispose?.Invoke();
        }
    }
}