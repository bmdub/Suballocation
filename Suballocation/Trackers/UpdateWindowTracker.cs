
namespace Suballocation.Trackers;

/// <summary>
/// Provides the ability to track rented memory segments from a suballocator. Specifically, the portions of the buffer that were allocated or updated.
/// Also can combine update windows automatically based on a configurable distance threshold.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units allocated.</typeparam>
public class UpdateWindowTracker<T> where T : unmanaged
{
    private static readonly Comparer<NativeMemorySegment<T>> _segmentComparer;
    private readonly List<NativeMemorySegment<T>> _segments = new();
    private readonly double _minimumFillPercentage;

    static unsafe UpdateWindowTracker()
    {
        _segmentComparer = Comparer<NativeMemorySegment<T>>.Create((a, b) => ((IntPtr)a.PElems).CompareTo((IntPtr)b.PElems));
    }

    /// <summary></summary>
    /// <param name="minimumFillPercentage">A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined.</param>
    public UpdateWindowTracker(double minimumFillPercentage)
    {
        _minimumFillPercentage = minimumFillPercentage;
    }

    /// <summary>A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined.</summary>
    public double MinimumFillPercentage => _minimumFillPercentage;

    /// <summary>Tells the tracker to note this newly-rented or updated segment.</summary>
    /// <param name="segment"></param>
    public void TrackAdditionOrUpdate(NativeMemorySegment<T> segment)
    {
        _segments.Add(segment);
    }

    /// <summary>Tells the tracker to note this removed segment.</summary>
    /// <param name="segment"></param>
    public unsafe void TrackRemoval(NativeMemorySegment<T> segment)
    {
        _segments.Add(new NativeMemorySegment<T>(0, segment.PElems, -1));
    }

    /// <summary>Used to determine if we can combine two update windows.</summary>
    private unsafe bool CanCombine(T* pElemsPrev, long sizePrev, T* pElemsNext, long sizeNext)
    {
        return (sizeNext + sizePrev) / (double)((long)pElemsNext + sizeNext - (long)pElemsPrev) >= _minimumFillPercentage;
    }

    /// <summary>Builds the final set of optimized/combined update windows based on the registered segment updates.</summary>
    /// <returns></returns>
    public unsafe UpdateWindows<T> BuildUpdateWindows()
    {
        // Sort segments by offset.
        _segments.Sort(_segmentComparer);

        List<NativeMemorySegment<T>> finalWindows = new List<NativeMemorySegment<T>>(_segments.Count);

        long bytesFilled = 0;
        foreach (var window in _segments)
        {
            if (window.Length == -1)
            {
                if (finalWindows.Count > 0 && finalWindows[^1].PElems == window.PElems)
                {
                    // Remove the previous matching segment, since this is a 'remove' operation.
                    bytesFilled -= finalWindows[^1].LengthBytes;

                    finalWindows.RemoveAt(finalWindows.Count - 1);
                }
            }
            else if (finalWindows.Count > 0 && CanCombine(finalWindows[^1].PElems, bytesFilled, window.PElems, window.LengthBytes))
            {
                // Combine the segment with the current update window.
                finalWindows[^1] = new NativeMemorySegment<T>()
                {
                    PElems = finalWindows[^1].PElems,
                    Length = ((long)window.PElems + window.LengthBytes - (long)finalWindows[^1].PElems) / Unsafe.SizeOf<T>()
                };

                // Make sure to account for the case of overlap.
                bytesFilled = Math.Min(bytesFilled + window.LengthBytes, finalWindows[^1].LengthBytes);
            }
            else
            {
                // Can't combine with another window; add as a new update window.
                finalWindows.Add(new NativeMemorySegment<T>() { PElems = window.PElems, Length = window.Length });

                bytesFilled = window.LengthBytes;
            }
        }

        return new UpdateWindows<T>(finalWindows);
    }

    /// <summary>Clears the tracker of all state, so it can be reused.</summary>
    public void Clear()
    {
        _segments.Clear();
    }
}
