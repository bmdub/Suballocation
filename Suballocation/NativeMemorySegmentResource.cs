﻿using Suballocation.Suballocators;
using System.Collections;

namespace Suballocation;

[DebuggerDisplay("[0x{(ulong)_ptr}] Length: {_length}, Value: {this[0]}")]
public unsafe readonly record struct NativeMemorySegmentResource<T> : ISegmentResource, ISegment<T> where T : unmanaged
{
    private readonly ISuballocator<T> _suballocator;
    private readonly IntPtr _ptr;
    private readonly long _length;

    public unsafe NativeMemorySegmentResource(ISuballocator<T> memoryPool, T* ptr, long length)
    {
        _suballocator = memoryPool;
        _ptr = (IntPtr)ptr;
        _length = length;
    }

    public ISuballocator<T> Suballocator { get => _suballocator; init => _suballocator = value; }
    ISuballocator ISegmentResource.Suballocator { get => Suballocator; }

    public unsafe void* PBytes { get => (void*)_ptr; init => _ptr = (IntPtr)value; }

    public long LengthBytes => _length * Unsafe.SizeOf<T>();

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
        $"[0x{(ulong)_ptr}] Length: {_length:N0}, Value: {this[0]}";

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
        _suballocator.ReturnResource(this);
    }
}
