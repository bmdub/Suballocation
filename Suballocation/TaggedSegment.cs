using Suballocation.Collections;
using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Light-weight, disposable structure that describes a segment of unmanaged memory allocated from a suballocator, and allows a user Tag element.
/// Note that this class is unsafe, and most forms of validation are intentionally omitted. Use at your own risk.
/// </summary>
[DebuggerDisplay("[0x{(ulong)_segmentPtr}] Length: {_length}, Value: {this[0]}")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe readonly record struct TaggedSegment<TElem, TSeg, TTag> : ISegment, IRangedEntry, IEnumerable<TElem> where TElem : unmanaged where TSeg : ISegment<TElem>
{
    private readonly TSeg _segment;
    private readonly TTag _tag;

    /// <summary></summary>
    /// <param name="segment">A base segment to build off of.</param>
    /// <param name="tag">Tag to associate with this segment.</param>
    public unsafe TaggedSegment(TSeg segment, TTag tag)
    {
        // Using pointers instead of references to avoid GC overhead.
        _segment = segment;
        _tag = tag;
    }

    /// <summary>A base segment to build off of.</summary>
    public TSeg Segment { get => _segment; init => _segment = value; }

    /// <summary>A tag item associated by the user to this segment allocation.</summary>
    public TTag Tag { get => _tag; init => _tag = value; }

    // Pass through the other implementations to the Segment.
    public TElem* PBuffer => _segment.PBuffer;
    public unsafe TElem* PSegment => _segment.PSegment;
    public long Length => _segment.Length;
    public ref TElem Value => ref _segment.Value;
    public ref TElem this[long index] => ref _segment[index];
    public ISuballocator<TElem>? Suballocator => _segment.Suballocator;
    public void* PWindowBytes => _segment.PWindowBytes;
    public void* PBufferBytes => _segment.PBufferBytes;
    public long LengthBytes => _segment.LengthBytes;
    ISuballocator ISegment.Suballocator => Suballocator!;
    public long RangeOffset => _segment.RangeOffset;
    public long RangeLength => _segment.RangeLength;
    public Span<TElem> AsSpan() => _segment.AsSpan();
    public override string ToString() => _segment.ToString()!;
    public IEnumerator<TElem> GetEnumerator() => _segment.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Dispose() => _segment.Dispose();
}

public static class SegmentExtensions
{
    public static TaggedSegment<TElem, TSeg, TTag> ToTagged<TElem, TSeg, TTag>(this TSeg segment, TTag tag) where TElem : unmanaged where TSeg : ISegment<TElem>
    {
        return new TaggedSegment<TElem, TSeg, TTag>(segment, tag);
    }
}