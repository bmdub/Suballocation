
namespace Suballocation.Trackers;

public class UpdateWindowTracker<TSeg> : UpdateWindowTracker<TSeg, EmptyStruct> where TSeg : unmanaged
{
    public UpdateWindowTracker(double minimumFillPercentage) : base(minimumFillPercentage) { }
}

/// <summary>
/// Provides the ability to track rented memory segments from a suballocator. Specifically, the portions of the buffer that were allocated or updated.
/// </summary>
/// <typeparam name="TSeg">A blittable element type that defines the units of a suballocation.</typeparam>
/// <typeparam name="TTag">Tag type for each the segments.</typeparam>
public class UpdateWindowTracker<TSeg, TTag> : ISegmentTracker<TSeg, TTag> where TSeg : unmanaged
{
    private static readonly Comparer<NativeMemorySegment<TSeg, TTag>> _segmentComparer;
    private readonly List<NativeMemorySegment<TSeg, TTag>> _segments = new();
    private readonly double _minimumFillPercentage;

    static unsafe UpdateWindowTracker()
    {
        _segmentComparer = Comparer<NativeMemorySegment<TSeg, TTag>>.Create((a, b) => ((IntPtr)a.PSegment).CompareTo((IntPtr)b.PSegment));
    }

    /// <summary></summary>
    /// <param name="minimumFillPercentage">A threshold between 0 and 1 that defines when to combine update windows for efficiency. 
    /// If any two update windows have a "used" to "combined length" ratio above this, then they will be combined into 1 update window.</param>
    public UpdateWindowTracker(double minimumFillPercentage)
    {
        _minimumFillPercentage = minimumFillPercentage;
    }

    /// <summary>A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined into 1 update window.</summary>
    public double MinimumFillPercentage => _minimumFillPercentage;

    public void TrackRental(NativeMemorySegment<TSeg, TTag> segment)
    {
        _segments.Add(segment);
    }

    public void TrackUpdate(NativeMemorySegment<TSeg, TTag> segment)
    {
        _segments.Add(segment);
    }

    public unsafe void TrackReturn(NativeMemorySegment<TSeg, TTag> segment)
    {
        // Use a null buffer pointer to indicate down the road that this update was a removal.
        _segments.Add(new NativeMemorySegment<TSeg, TTag>(null, segment.PSegment, segment.Length, segment.Tag));
    }

    /// <summary>Used to determine if we can combine two update windows.</summary>
    private unsafe bool CanCombine(TSeg* pElemsPrev, long sizePrev, TSeg* pElemsNext, long sizeNext)
    {
        return (sizeNext + sizePrev) / (double)((long)pElemsNext + sizeNext - (long)pElemsPrev) >= _minimumFillPercentage;
    }

    /// <summary>Builds the final set of optimized/combined update windows based on the registered segment updates.</summary>
    /// <returns></returns>
    public unsafe UpdateWindows<TSeg, TTag> BuildUpdateWindows()
    {
        // Sort segments by offset, see where we can combine them, and return them.
        _segments.Sort(_segmentComparer);

        List<NativeMemorySegment<TSeg, TTag>> finalWindows = new List<NativeMemorySegment<TSeg, TTag>>(_segments.Count);

        long bytesFilled = 0;
        foreach (var window in _segments)
        {
            if (window.PBuffer == null)
            {
                // Remove the previous matching segment, if the subsequent segment is a removal of the same segment.
                if (finalWindows.Count > 0 && finalWindows[^1].PSegment == window.PSegment && finalWindows[^1].Length == window.Length)
                {
                    bytesFilled -= finalWindows[^1].LengthBytes;

                    finalWindows.RemoveAt(finalWindows.Count - 1);
                }
            }
            else if (finalWindows.Count > 0 && CanCombine(finalWindows[^1].PSegment, bytesFilled, window.PSegment, window.LengthBytes))
            {
                // We can combine the segment with the latest update window.
                finalWindows[^1] = new NativeMemorySegment<TSeg, TTag>()
                {
                    PSegment = finalWindows[^1].PSegment,
                    Length = ((long)window.PSegment + window.LengthBytes - (long)finalWindows[^1].PSegment) / Unsafe.SizeOf<TSeg>()
                };

                // Make sure to account for the case of overlap.
                bytesFilled = Math.Min(bytesFilled + window.LengthBytes, finalWindows[^1].LengthBytes);
            }
            else
            {
                // Can't combine with another window; add as a new update window.
                finalWindows.Add(new NativeMemorySegment<TSeg, TTag>() { PSegment = window.PSegment, Length = window.Length });

                bytesFilled = window.LengthBytes;
            }
        }

        return new UpdateWindows<TSeg, TTag>(finalWindows);
    }

    public void Clear()
    {
        _segments.Clear();
    }
}
