using System;
using System.Runtime.InteropServices;

namespace ShaderExtends.Base
{
    public unsafe class NativeVertexBuffer
    {
        private byte* _basePtr;
        private int _capacity;
        private int _used;

        public NativeVertexBuffer(int initialCapacity = 1024 * 1024 * 4)
        {
            _capacity = initialCapacity;
            _basePtr = (byte*)NativeMemory.Alloc((nuint)_capacity);
            _used = 0;
        }

        public bool EnsureCapacity(int byteSize)
        {
            if (_used + byteSize > _capacity)
            {
                Grow(byteSize);
                return true;
            }
            return false;
        }

        public int GetCapacity() => _capacity;

        public IntPtr GetBasePtr() => (IntPtr)_basePtr;

        public IntPtr Rent(int byteSize)
        {
            if (_used + byteSize > _capacity) Grow(byteSize);
            IntPtr res = (IntPtr)(_basePtr + _used);
            _used += byteSize;
            return res;
        }

        public void Reset() => _used = 0;

        private void Grow(int needed)
        {
            int newCapacity = Math.Max(_capacity * 2, _capacity + needed);
            _basePtr = (byte*)NativeMemory.Realloc(_basePtr, (nuint)newCapacity);
            _capacity = newCapacity;
        }

        public void Dispose()
        {
            if (_basePtr != null)
            {
                NativeMemory.Free(_basePtr);
                _basePtr = null;
            }
        }
    }
}