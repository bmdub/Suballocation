using Suballocation.Suballocators;
using System.Collections;

namespace Suballocation;

/// <summary>
/// Disposable structure that represents a segment of unmanaged memory allocated from a suballocator.
/// </summary>
[DebuggerDisplay("[0x{(ulong)_ptr}] Length: {_length}, Value: {this[0]}")]
public unsafe readonly record struct NativeMemorySegmentResource<T> : ISegmentResource, ISegment<T> where T : unmanaged
{
    private readonly NativeMemorySegment<T> _segment;
    private readonly ISuballocator<T> _suballocator;

    public unsafe NativeMemorySegmentResource(in NativeMemorySegment<T> segment, ISuballocator<T> memoryPool)
    {
        _segment = segment;
        _suballocator = memoryPool;
    }

    public ISuballocator<T> Suballocator { get => _suballocator; init => _suballocator = value; }
    ISuballocator ISegmentResource.Suballocator { get => Suballocator; }

    public unsafe void* PBytes { get => _segment.PBytes; }

    public long LengthBytes => _segment.LengthBytes;

    public unsafe T* PElems { get => _segment.PElems; }

    public long Length { get => _segment.Length; }

    public ref T Value => ref _segment.Value;

    public ref T this[long index] => ref _segment[index];

    public Span<T> AsSpan() => _segment.AsSpan();

    public override string ToString() => _segment.ToString();

    public IEnumerator<T> GetEnumerator() => _segment.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        _suballocator.Return(_segment);
    }
}

public static class NativeMemorySegmentResource_SuballocatorExtensions
{
    /// <summary>Returns a free segment of memory of the desired length, as an IDisposable resource.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <returns>A rented segment that must be disposed in the future to free the memory for subsequent usage.</returns>
    public static NativeMemorySegmentResource<T> RentResource<T>(this ISuballocator<T> suballocator, long length = 1) where T : unmanaged =>
        new NativeMemorySegmentResource<T>(suballocator.Rent(length), suballocator);
}