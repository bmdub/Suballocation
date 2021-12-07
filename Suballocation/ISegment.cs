
using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Provides basic information about a rented segment from a suballocator.
/// </summary>
public interface ISegment : IDisposable
{
    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    unsafe void* PBytes { get; }

    /// <summary>The total size of segment.</summary>
    long LengthBytes { get; }

    /// <summary>The suballocator from which this segment was rented.</summary>
    ISuballocator Suballocator { get; }
}