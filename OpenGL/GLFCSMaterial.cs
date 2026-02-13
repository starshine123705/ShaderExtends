using Microsoft.Xna.Framework.Graphics;
using OpenTK.Graphics.OpenGL4;
using ShaderExtends.Base;
using ShaderExtends.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ShaderExtends.OpenGL
{
    public unsafe class GLFCSMaterial : IFCSMaterial
    {
        public int GLProgram { get; set; }
        public int GLCS { get; set; }

        public Dictionary<string, FCSParameter> Parameters { get; } = [];

        /// <summary>
        /// 从 Effect 获取顶点布局
        /// </summary>
        /// 
        public SpriteVertexWriter VertexWriter { get; private set; }
        public int VertexStride { get; private set; }

        public IShadowBuffer Shadow { get; private set; }
        public int GroupsX { get; private set; }
        public int GroupsY { get; private set; }
        public int GroupsZ { get; private set; }
        public GLFCSEffect Effect { get; }

        private Texture[] _sourceTexture;   

        public Texture[] SourceTexture => _sourceTexture;

        private readonly Dictionary<int, byte[]> _cpuBuffers = new();
        private readonly Dictionary<int, int> _ubos = new();
        private readonly HashSet<int> _dirtySlots = new();
        private readonly Action _onDispose;

        public GLFCSMaterial(GLFCSEffect effect, Action onDispose)
        {
            Effect = effect;
            GLProgram = effect.GLProgram;
            GLCS = effect.GLCS;
            _onDispose = onDispose;

            int maxSlot = 0;
            if (effect.Metadata.Textures.Count > 0)
            {
                maxSlot = effect.Metadata.Textures.Max(t => t.Slot);
            }
            _sourceTexture = new Texture[maxSlot + 1];

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
                        this
                    );
                }
            }
            var lastEl = effect.Metadata.InputElements.OrderByDescending(e => e.AlignedByteOffset).FirstOrDefault();
            VertexStride = lastEl != null ? lastEl.AlignedByteOffset + IFCSMaterial.GetFormatSize(lastEl.Format) : 0;

            // 2. 创建极致优化的顶点写入委托
            if (effect.Metadata.InputElements.Count > 0)
            {
                VertexWriter = SpriteVertexWriterFactory.Create(effect.Metadata.InputElements, VertexStride);
            }
        }

        public void UpdateBuffer(int slot, byte[] data)
        {
            if (_cpuBuffers.ContainsKey(slot))
            {
                _cpuBuffers[slot] = data;
                _dirtySlots.Add(slot);
            }
        }

        public void InternalUpdate(int slot, int offset, void* src, int size)
        {
            if (!_cpuBuffers.TryGetValue(slot, out var buffer)) return;
            if (offset + size > buffer.Length) return;

            fixed (byte* pBuf = buffer)
            {
                Unsafe.CopyBlock(pBuf + offset, src, (uint)size);
            }
            _dirtySlots.Add(slot);
        }

        public void SyncToDevice()
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

        public void EnsureShadow(int w, int h, int d = -1)
        {
            if (Shadow != null && Shadow.Width == w && Shadow.Height == h) return;

            Shadow?.Dispose();
            Shadow = new GLShadowBuffer(w, h);

            GroupsX = (w + Effect.Metadata.ThreadX - 1) / Effect.Metadata.ThreadX;
            GroupsY = (h + Effect.Metadata.ThreadY - 1) / Effect.Metadata.ThreadY;
            GroupsZ = d == -1 ? 1 : (d + Effect.Metadata.ThreadZ - 1) / Effect.Metadata.ThreadZ;
        }

        public int GetUbo(int slot) => _ubos.TryGetValue(slot, out int ubo) ? ubo : 0;

        public void Apply(IFNARenderDriver driver)
        {
            driver.CurrentMaterial = this;
            SyncToDevice();

            if (GLProgram != 0)
            {
                GL.UseProgram(GLProgram);

                foreach (var buffer in Effect.Metadata.Buffers)
                {
                    int ubo = GetUbo(buffer.Slot);
                    if (ubo != 0)
                    {
                        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, buffer.Slot, ubo);
                    }
                }
            }

            if (GLCS != 0)
            {
                GL.UseProgram(GLCS);

                foreach (var buffer in Effect.Metadata.Buffers)
                {
                    int ubo = GetUbo(buffer.Slot);
                    if (ubo != 0)
                    {
                        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, buffer.Slot, ubo);
                    }
                }
            }
        }

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