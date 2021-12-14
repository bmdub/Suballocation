using Suballocation.Collections;

namespace Suballocation.Trackers;

/// <summary>
/// Provides the ability to approximately track fragmented segments in a buffer.
/// </summary>
/// <typeparam name="TElem">A blittable element type that defines the units of a suballocation.</typeparam>
/// <typeparam name="TSeg">The segment type being tracked.</typeparam>
public class FragmentationTracker<TElem, TSeg> : ISegmentTracker<TElem, TSeg> where TElem : unmanaged where TSeg : ISegment<TElem>
{ 
    private readonly OrderedRangeBucketDictionary<TSeg> _dict;

    /// <summary></summary>
    /// <param name="length">The unit length of the unmanaged buffer that you are tracking segments for.</param>
    /// <param name="bucketLength">The unit length that each backing bucket is intended to manage. Smaller buckets may improve ordered-lookup performance for non-sparse elements at the cost of GC overhead and memory.</param>
    public FragmentationTracker(long length, long bucketLength)
    {
        _dict = new OrderedRangeBucketDictionary<TSeg>(0, length - 1, bucketLength);
    }

    public void TrackRental(TSeg segment)
    {
        _dict.Add(segment);
    }

    public void TrackUpdate(TSeg segment)
    {
        _dict[segment.RangeOffset] = segment;
    }

    public void TrackReturn(TSeg segment)
    {
        if (_dict.Remove(segment.RangeOffset, out _) == false)
        {
            throw new KeyNotFoundException();
        }
    }

    /// <summary>Searches the collection for segments that are fragmented, and returns them, unordered.</summary>
    /// <param name="minimumFragmentationPct">The fragmentation threshold, from 0 to 1, at which segments are deemed fragmented.</param>
    /// <returns>The segments that are found to be fragmented.</returns>
    public IEnumerable<TSeg> GetFragmentedSegments(double minimumFragmentationPct)
    {
        foreach(var bucket in _dict.GetBuckets())
        {
            if (bucket.FillPct > 0 && 1.00001 - bucket.FillPct >= minimumFragmentationPct)
            {
                foreach (var entry in bucket.GetOriginatingRanges())
                {
                    yield return entry;
                }
            }
        }
    }

    public void Clear()
    {
        _dict.Clear();
    }
}
