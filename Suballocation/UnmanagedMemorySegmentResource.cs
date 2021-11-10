using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation
{
    [DebuggerDisplay("[0x{(ulong)_ptr}] Length: {_length}, Size: {Size}, Value: {this[0]}")]
    public unsafe readonly record struct UnmanagedMemorySegmentResource<T> : ISegmentResource, ISegment<T> where T : unmanaged
    {
        private readonly ISuballocator<T> _memoryPool;
        private readonly IntPtr _ptr;
        private readonly long _length;

        public unsafe UnmanagedMemorySegmentResource(ISuballocator<T> memoryPool, T* ptr, long length)
        {
            _memoryPool = memoryPool;
            _ptr = (IntPtr)ptr;
            _length = length;
        }

        public ISuballocator<T> MemoryPool { get => _memoryPool; init => _memoryPool = value; }
        ISuballocator ISegmentResource.MemoryPool { get => MemoryPool; }

        public unsafe void* PBytes { get => (void*)_ptr; init => _ptr = (IntPtr)value; }

        public long Size => _length * Unsafe.SizeOf<T>();

        public unsafe T* PElems { get => (T*)_ptr; init => _ptr = (IntPtr)value; }

        public long Length { get => _length; init => _length = value; }

        public ref T Value => ref *(T*)_ptr;

        public ref T this[long index] => ref ((T*)_ptr)[index];

        public Span<T> AsSpan()
        {
            if (_length > int.MaxValue) throw new InvalidOperationException($"Unable to return a Span<T> for a range that is larger than int.Maxvalue.");

            return new Span<T>((T*)_ptr, (int)_length);
        }

        public override string ToString() =>
            $"[0x{(ulong)_ptr}] Length: {_length:N0}, Size: {Size:N0}, Value: {this[0]}";

        public IEnumerator<T> GetEnumerator()
        {
            for (long i = 0; i < _length; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public void Dispose()
        {
            _memoryPool.ReturnResource(this);
        }
    }
}
