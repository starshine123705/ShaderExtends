using Microsoft.Xna.Framework;
using ShaderExtends.Interfaces;

namespace ShaderExtends.Base
{
    public unsafe class FCSParameter
    {
        private readonly int _offset, _size, _slot;
        private readonly IFCSMaterial _owner;

        internal FCSParameter(string name, int offset, int size, int slot, IFCSMaterial owner)
        {
            _offset = offset; _size = size; _slot = slot; _owner = owner;
        }

        public void SetValue(float v) => _owner.InternalUpdate(_slot, _offset, &v, 4);
        public void SetValue(Vector2 v) => _owner.InternalUpdate(_slot, _offset, &v, 8);
        public void SetValue(Matrix v) => _owner.InternalUpdate(_slot, _offset, &v, 64);
        public void SetValue(Vector3 v) => _owner.InternalUpdate(_slot, _offset, &v, 12);
        public void SetValue(Vector4 v) => _owner.InternalUpdate(_slot, _offset, &v, 16);
        public void SetValue(Color c)
        {
            var v = c.ToVector4();
            _owner.InternalUpdate(_slot, _offset, &v, 16);
        }
    }
}