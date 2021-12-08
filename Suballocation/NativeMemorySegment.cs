using Suballocation.Collections;
using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Lightweight structure that represents a segment of unmanaged memory allocated from a suballocator.
/// Note that this class is unsafe, and most forms of validation are intentionally omitted. Use at your own risk.
/// </summary>
[DebuggerDisplay("[0x{(ulong)_segmentPtr}] Length: {_length}, Value: {this[0]}")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe readonly record struct NativeMemorySegment<TSeg, TTag> : ISegment, IRangedEntry, IEnumerable<TSeg> where TSeg : unmanaged
{
    private readonly IntPtr _bufferPtr;
    private readonly IntPtr _segmentPtr;
    private readonly long _length;
    private readonly TTag _tag;

    /// <summary></summary>
    /// <param name="bufferPtr">A pointer to the start of the containing memory buffer.</param>
    /// <param name="segmentPtr">A pointer to the start of the memory segment.</param>
    /// <param name="length">The unit length of the segment.</param>
    /// <param name="tag">Optional tag to associate with this segment.</param>
    public unsafe NativeMemorySegment(TSeg* bufferPtr, TSeg* segmentPtr, long length, TTag tag = default!)
    {
        // Using pointers instead of references to avoid GC overhead.
        _bufferPtr = (IntPtr)bufferPtr;
        _segmentPtr = (IntPtr)segmentPtr;
        _length = length;
        _tag = tag;
    }

    /// <summary>Pointer to the start of the buffer that contains this segment.</summary>
    public TSeg* PBuffer { get => (TSeg*)_bufferPtr; init => _bufferPtr = (IntPtr)value; }

    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    public unsafe TSeg* PSegment { get => (TSeg*)_segmentPtr; init => _segmentPtr = (IntPtr)value; }

    /// <summary>The total unit length of segment.</summary>
    public long Length { get => _length; init => _length = value; }

    /// <summary>A reference to the first or only value of the segment.</summary>
    public ref TSeg Value => ref *(TSeg*)_segmentPtr;

    /// <summary>A reference to the ith element of the segment.</summary>
    public ref TSeg this[long index] => ref ((TSeg*)_segmentPtr)[index];

    /// <summary>A tag item associated by the user to this segment allocation.</summary>
    public TTag Tag { get => _tag; init => _tag = value; }

    /// <summary>The suballocator that allocated this segment, or Null if not found or disposed.</summary>
    public ISuballocator<TSeg, TTag>? Suballocator 
    { 
        get        
        {
            if(SuballocatorTable<TSeg, TTag>.TryGetByBufferAddress(_bufferPtr, out var suballocator) == false)
            {
                return null;
            }

            return suballocator;
        }
    }

    public void* PSegmentBytes { get => (void*)_segmentPtr; }

    public void* PBufferBytes { get => (void*)_bufferPtr; }

    public long LengthBytes => _length * Unsafe.SizeOf<TSeg>();

    ISuballocator ISegment.Suballocator => Suballocator!;

    public long RangeOffset => ((long)_segmentPtr - (long)_bufferPtr) / Unsafe.SizeOf<TSeg>();

    public long RangeLength => Length;

    /// <summary>A Span<typeparamref name="TSeg"/> on top of the segment.</summary>
    public Span<TSeg> AsSpan()
    {
        if (_length > int.MaxValue) throw new InvalidOperationException($"Unable to return a Span<T> for a range that is larger than int.Maxvalue.");

        return new Span<TSeg>((TSeg*)_segmentPtr, (int)_length);
    }

    public override string ToString() =>
        $"[0x{(ulong)_segmentPtr}] Length: {_length:N0}, Value: {this[0]}";

    public IEnumerator<TSeg> GetEnumerator()
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
        Suballocator?.Return(this);
    }
}