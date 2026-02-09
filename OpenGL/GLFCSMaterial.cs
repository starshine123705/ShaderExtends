using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ShaderExtends.OpenGL
{
    public class GLFCSMaterial : IFCSMaterial
    {
        public int GLProgram { get; set; }
        public int GLCS { get; set; }

        public Dictionary<string, FCSParameter> Parameters { get; } = [];

        public IShadowBuffer Shadow { get; private set; }
        public int GroupsX { get; private set; }
        public int GroupsY { get; private set; }

        public GLFCSEffect Effect { get; }

        private readonly Dictionary<int, byte[]> _cpuBuffers = new Dictionary<int, byte[]>();
        private readonly Dictionary<int, int> _ubos = new Dictionary<int, int>();
        private readonly HashSet<int> _dirtySlots = new HashSet<int>();
        private readonly Action _onDispose;

        public GLFCSMaterial(GLFCSEffect effect, Action onDispose)
        {
            Effect = effect;
            GLProgram = effect.GLProgram;
            GLCS = effect.GLCS;

            foreach (var cb in effect.Metadata.Buffers)
            {
                _cpuBuffers[cb.Slot] = new byte[cb.TotalSize];

                _dirtySlots.Add(cb.Slot);

                foreach (var varMeta in cb.Variables)
                {
                    Parameters[varMeta.Key] = new FCSParameter(
                        varMeta.Key,
                        varMeta.Value.Offset,
                        varMeta.Value.Size,
                        cb.Slot,
                    );
                }
            }
            _onDispose = onDispose;
        }


        public void UpdateBuffer(int slot, byte[] data)
        {
            if (_cpuBuffers.ContainsKey(slot))
            {
                _cpuBuffers[slot] = data;
                _dirtySlots.Add(slot);
            }
        }

        public unsafe void InternalUpdate(int slot, int offset, void* src, int size)
        {
            if (!_cpuBuffers.TryGetValue(slot, out var buffer)) return;

            if (offset + size > buffer.Length) return;

            fixed (byte* pBuf = buffer)
            {
                Unsafe.CopyBlock(pBuf + offset, src, (uint)size);
            }
            _dirtySlots.Add(slot);
        }

        public void SyncToDevice(object notInUse = null)
        {
            foreach (var slot in _dirtySlots)
            {
                if (!_ubos.TryGetValue(slot, out int ubo))
                {
                    ubo = GL.GenBuffer();
                    _ubos[slot] = ubo;
                }

                byte[] data = _cpuBuffers[slot];

                GL.BindBuffer(BufferTarget.UniformBuffer, ubo);
                GL.BufferData(BufferTarget.UniformBuffer, data.Length, data, BufferUsageHint.DynamicDraw);
            }
            _dirtySlots.Clear();
            GL.BindBuffer(BufferTarget.UniformBuffer, 0);
        }

        public void EnsureShadow(int w, int h)
        {
            if (Shadow != null && Shadow.Width == w && Shadow.Height == h) return;

            Shadow?.Dispose();
            Shadow = new GLShadowBuffer(w, h);

            GroupsX = (int)((w + 15) / 16);
            GroupsY = (int)((h + 15) / 16);
        }

        public int GetUbo(int slot) => _ubos.TryGetValue(slot, out int ubo) ? ubo : 0;

        public void Dispose()
        {
            foreach (var ubo in _ubos.Values)
            {
                GL.DeleteBuffer(ubo);
            }
            Shadow?.Dispose();

            Effect.Release();

            _onDispose?.Invoke();
        }
    }
}