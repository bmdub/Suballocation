
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
    unsafe void* PSegmentBytes { get; }

    /// <summary>The total size of segment.</summary>
    long LengthBytes { get; }

    /// <summary>The suballocator from which this segment was rented.</summary>
    ISuballocator Suballocator { get; }
}