
namespace Suballocation.Trackers;

/// <summary>
/// Exposes suballocation segment tracking functionality.
/// </summary>
/// <typeparam name="TElem">A blittable element type that defines the units of a suballocation.</typeparam>
/// <typeparam name="TSeg">The segment type being tracked.</typeparam>
public interface ISegmentTracker<TElem, TSeg> where TSeg : ISegment<TElem> where TElem : unmanaged
{
    /// <summary>Tells the tracker to process this segment allocation event.</summary>
    /// <param name="segment">The added memory segment.</param>
    void TrackRental(TSeg segment);

    /// <summary>Tells the tracker to process this segment update event.</summary>
    /// <param name="segment">The updated memory segment.</param>
    void TrackUpdate(TSeg segment);

    /// <summary>Tells the tracker to note this segment deallocation event.</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    void TrackReturn(TSeg segment);

    /// <summary>Clears the state of the tracker.</summary>
    void Clear();
}
