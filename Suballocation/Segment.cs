using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Light-weight, disposable structure that describes a segment of unmanaged memory allocated from a suballocator.
/// Note that this class is unsafe, and most forms of validation are intentionally omitted. Use at your own risk.
/// </summary>
[DebuggerDisplay("[0x{(ulong)_segmentPtr}] Length: {_length}, Value: {this[0]}")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe readonly record struct Segment<T> : ISegment<T> where T : unmanaged
{
    private readonly IntPtr _bufferPtr;
    private readonly IntPtr _segmentPtr;
    private readonly long _length;

    /// <summary></summary>
    /// <param name="bufferPtr">A pointer to the start of the containing memory buffer.</param>
    /// <param name="segmentPtr">A pointer to the start of the memory segment.</param>
    /// <param name="length">The unit length of the segment.</param>
    public unsafe Segment(T* bufferPtr, T* segmentPtr, long length)
    {
        // Using pointers instead of references to avoid GC overhead.
        _bufferPtr = (IntPtr)bufferPtr;
        _segmentPtr = (IntPtr)segmentPtr;
        _length = length;
    }

    public T* BufferPtr { get => (T*)_bufferPtr; init => _bufferPtr = (IntPtr)value; }

    public unsafe T* SegmentPtr { get => (T*)_segmentPtr; init => _segmentPtr = (IntPtr)value; }

    public long Length { get => _length; init => _length = value; }

    public ref T Value => ref *(T*)_segmentPtr;

    public ref T this[long index] => ref ((T*)_segmentPtr)[index];

    public ISuballocator<T>? Suballocator
    {
        get
        {
            if (SuballocatorTable<T>.TryGetByBufferAddress(_bufferPtr, out var suballocator) == false)
            {
                return null;
            }

            return suballocator;
        }
    }

    public void* WindowBytesPtr { get => (void*)_segmentPtr; }

    public void* BufferBytesPtr { get => (void*)_bufferPtr; }

    public long LengthBytes => _length * Unsafe.SizeOf<T>();

    ISuballocator ISegment.Suballocator => Suballocator!;

    public long RangeOffset => ((long)_segmentPtr - (long)_bufferPtr) / Unsafe.SizeOf<T>();

    public long RangeLength => Length;

    public Span<T> AsSpan()
    {
        if (_length > int.MaxValue) throw new InvalidOperationException($"Unable to return a Span<T> for a range that is larger than int.Maxvalue.");

        return new Span<T>((T*)_segmentPtr, (int)_length);
    }

    public override string ToString() =>
        $"[0x{(ulong)_segmentPtr}] Length: {_length:N0}, Value: {this[0]}";

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
        Suballocator?.Return(SegmentPtr);
    }
}

public static class SuballocatorExtensions
{
    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <param name="segment">A rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</param>
    /// <returns>True if successful; False if free space could not be found for this segment.</returns>
    public static unsafe bool TryRentSegment<T>(this ISuballocator<T> suballocator, long length, out Segment<T> segment) where T : unmanaged
    {
        if (suballocator.TryRent(length, out var segmentPtr, out var actualLength) == false)
        {
            segment = default;
            return false;
        }

        segment = new Segment<T>(suballocator.PElems, segmentPtr, actualLength);
        return true;
    }

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again. Could be called in place of Dispose() on a segment.</summary>
    /// <param name="segment">A previously rented segment of memory from this allocator.</param>
    public static unsafe void ReturnSegment<T>(this ISuballocator<T> suballocator, Segment<T> segment) where T : unmanaged
    {
        suballocator.Return(segment.SegmentPtr);
    }

    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <returns>A rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</returns>
    public static Segment<T> RentSegment<T>(this ISuballocator<T> suballocator, long length = 1) where T : unmanaged
    {
        if (TryRentSegment(suballocator, length, out var segment) == false)
        {
            throw new OutOfMemoryException();
        }

        return segment;
    }
}