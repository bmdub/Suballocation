using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Provides basic information about a segment resource.
/// </summary>
public interface ISegmentResource : IDisposable
{
    /// <summary>The suballocator from which this segment was rented.</summary>
    ISuballocator Suballocator { get; }
}
