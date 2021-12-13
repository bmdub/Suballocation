
using Suballocation.Collections;
using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Exposes basic information about a segment rented from a suballocator.
/// </summary>
public interface ISegment : IDisposable
{
    /// <summary>Pointer to the start of the buffer that contains this segment.</summary>
    unsafe void* PBufferBytes { get; }

    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    unsafe void* PWindowBytes { get; }

    /// <summary>The total size of segment.</summary>
    long LengthBytes { get; }

    /// <summary>The suballocator from which this segment was rented.</summary>
    ISuballocator Suballocator { get; }
}

/// <summary>
/// Exposes information about a segment rented from a suballocator.
/// </summary>
public unsafe interface ISegment<T> : IRangedEntry, IEnumerable<T>, ISegment where T : unmanaged
{
    /// <summary>Pointer to the start of the buffer that contains this segment.</summary>
    public T* PBuffer { get; }

    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    public unsafe T* PSegment { get; }

    /// <summary>The total unit length of segment.</summary>
    public long Length { get; }

    /// <summary>A reference to the first or only value of the segment.</summary>
    public ref T Value { get; }

    /// <summary>A reference to the ith element of the segment.</summary>
    public ref T this[long index] { get; }

    /// <summary>The suballocator that allocated this segment, or Null if not found or disposed.</summary>
    public new ISuballocator<T>? Suballocator { get; }

    /// <summary>A Span<typeparamref name="T"/> on top of the segment.</summary>
    public Span<T> AsSpan();
}