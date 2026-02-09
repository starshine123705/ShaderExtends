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

        private readonly Dictionary<int, byte[]> _cpuBuffers = new();
        private readonly Dictionary<int, ID3D11Buffer> _gpuBuffers = new();
        private readonly HashSet<int> _dirtySlots = new HashSet<int>();
        private readonly Action _onDispose;

        private readonly ID3D11Device _device;

        public IShadowBuffer Shadow { get; private set; }
        public int GroupsX { get; private set; }
        public int GroupsY { get; private set; }

        public D3D11FCSMaterial(ID3D11Device device, D3D11FCSEffect effect, Action onDispose)
        {
            _device = device;
            Effect = effect;

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
            _onDispose = onDispose;
        }


        public void UpdateBuffer(int slot, byte[] data)
        {
            _cpuBuffers[slot] = data;
            _dirtySlots.Add(slot);
        }

        public void SyncToDevice(object deviceContext)
        {
            var context = (ID3D11DeviceContext)deviceContext;
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
            GroupsX = (int)((w + 15) / 16);
            GroupsY = (int)((h + 15) / 16);
        }

        public ID3D11Buffer GetBuffer(int slot) => _gpuBuffers.TryGetValue(slot, out var b) ? b : null;

        public void InternalUpdate(int slot, int offset, void* src, int size)
        {
            fixed (byte* pBuf = _cpuBuffers[slot])
                Buffer.MemoryCopy(src, pBuf + offset, size, size);
            _dirtySlots.Add(slot);
        }

        public void Dispose()
        {
            foreach (var b in _gpuBuffers.Values) b.Dispose();
            Shadow?.Dispose();
            Effect.Release();
        }
    }
}