
using Suballocation.Suballocators;

namespace Suballocation.Trackers;

/// <summary>
/// Provides the ability to track rented memory segments from a suballocator. Specifically, the portions of the buffer that were allocated or updated.
/// </summary>
/// <typeparam name="TElem">A blittable element type that defines the units of a suballocation.</typeparam>
/// <typeparam name="TSeg">The segment type being tracked.</typeparam>
public class UpdateWindowTracker<TElem, TSeg> : ISegmentTracker<TElem, TSeg> where TElem : unmanaged where TSeg : ISegment<TElem>
{
    private static readonly Comparer<(bool Added, TSeg Segment)> _segmentComparer;
    private readonly List<(bool Added, TSeg Segment)> _segments = new();
    private readonly double _minimumFillPercentage;

    static unsafe UpdateWindowTracker()
    {
        _segmentComparer = Comparer<(bool Added, TSeg Segment)>.Create((a, b) => ((IntPtr)a.Segment.PSegment).CompareTo((IntPtr)b.Segment.PSegment));
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

    public void TrackRental(TSeg segment)
    {
        _segments.Add((true, segment));
    }

    public void TrackUpdate(TSeg segment)
    {
        _segments.Add((true, segment));
    }

    public unsafe void TrackReturn(TSeg segment)
    {
        _segments.Add((false, segment));
    }

    /// <summary>Used to determine if we can combine two update windows.</summary>
    private unsafe bool CanCombine(TElem* pElemsPrev, long sizePrev, TElem* pElemsNext, long sizeNext)
    {
        return (sizeNext + sizePrev) / (double)((long)pElemsNext + sizeNext - (long)pElemsPrev) >= _minimumFillPercentage;
    }

    /// <summary>Builds the final set of optimized/combined update windows based on the registered segment updates.</summary>
    /// <returns></returns>
    public unsafe UpdateWindows<TElem> BuildUpdateWindows()
    {
        // Sort segments by offset, see where we can combine them, and return them.
        _segments.Sort(_segmentComparer);

        List<UpdateWindow<TElem>> finalWindows = new List<UpdateWindow<TElem>>(_segments.Count);

        long bytesFilled = 0;
        foreach (var window in _segments)
        {
            if (window.Added == false)
            {
                // Remove the previous matching segment, if the subsequent segment is a removal of the same segment.
                if (finalWindows.Count > 0 && finalWindows[^1].PSegment == window.Segment.PSegment && finalWindows[^1].Length == window.Segment.Length)
                {
                    bytesFilled -= finalWindows[^1].LengthBytes;

                    finalWindows.RemoveAt(finalWindows.Count - 1);
                }
            }
            else if (finalWindows.Count > 0 && CanCombine(finalWindows[^1].PSegment, bytesFilled, window.Segment.PSegment, window.Segment.LengthBytes))
            {
                // We can combine the segment with the latest update window.
                finalWindows[^1] = new UpdateWindow<TElem>()
                {
                    PSegment = finalWindows[^1].PSegment,
                    Length = ((long)window.Segment.PSegment + window.Segment.LengthBytes - (long)finalWindows[^1].PSegment) / Unsafe.SizeOf<TElem>()
                };

                // Make sure to account for the case of overlap.
                bytesFilled = Math.Min(bytesFilled + window.Segment.LengthBytes, finalWindows[^1].LengthBytes);
            }
            else
            {
                // Can't combine with another window; add as a new update window.
                finalWindows.Add(new UpdateWindow<TElem>() { PSegment = window.Segment.PSegment, Length = window.Segment.Length });

                bytesFilled = window.Segment.LengthBytes;
            }
        }

        return new UpdateWindows<TElem>(finalWindows);
    }

    public void Clear()
    {
        _segments.Clear();
    }
}
