
namespace Suballocation.Trackers;

public interface ISegmentTracker<TSeg, TTag> where TSeg : unmanaged
{
    /// <summary>Tells the tracker to process this segment allocation event.</summary>
    /// <param name="segment">The added memory segment.</param>
    void TrackRental(NativeMemorySegment<TSeg, TTag> segment);

    /// <summary>Tells the tracker to process this segment update event.</summary>
    /// <param name="segment">The updated memory segment.</param>
    void TrackUpdate(NativeMemorySegment<TSeg, TTag> segment);

    /// <summary>Tells the tracker to note this segment deallocation event.</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    void TrackReturn(NativeMemorySegment<TSeg, TTag> segment);

    /// <summary>Clears the state of the tracker.</summary>
    void Clear();
}
