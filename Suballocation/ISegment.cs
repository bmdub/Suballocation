
using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Provides basic information about a rented segment from a suballocator.
/// </summary>
public interface ISegment
{
    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    unsafe void* PBytes { get; }

    /// <summary>The total size of segment.</summary>
    public long LengthBytes { get; }
}

/// <summary>
/// Provides information about a rented segment from a suballocator.
/// </summary>
public interface ISegment<T> : ISegment, IEnumerable<T> where T : unmanaged
{
    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    unsafe T* PElems { get; }

    /// <summary>The total unit length of segment.</summary>
    long Length { get; }

    /// <summary>A reference to the first or only value of the segment.</summary>
    ref T Value { get; }

    /// <summary>A reference to the ith element of the segment.</summary>
    ref T this[long index] { get; }

    /// <summary>A Span<typeparamref name="T"/> over the first (up to) 2GB of the segment.</summary>
    Span<T> AsSpan();
}
