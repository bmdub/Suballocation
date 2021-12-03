using Suballocation.Collections;

namespace Suballocation;

/// <summary>
/// Provides the ability to approximately track fragmented segments in a buffer.
/// </summary>
/// <typeparam name="T">An item type to map to each segment, for later retrieval.</typeparam>
public class FragmentationTracker<T>
{
    private readonly OrderedRangeBucketDictionary<T> _dict;

    /// <summary></summary>
    /// <param name="keyMin">The minimum key value to allow in the collection. The key range dictates the size of a backing array; thus a smaller range is better.</param>
    /// <param name="keyMax">The maximum key value to allow in the collection, inclusive. The key range dictates the size of a backing array; thus a smaller range is better.</param>
    /// <param name="bucketLength">The key-range length that each backing bucket is intended to manage. Smaller buckets may improve ordered-lookup performance for non-sparse elements at the cost of GC overhead and memory.</param>
    public FragmentationTracker(long keyMin, long keyMax, long bucketLength)
    {
        _dict = new OrderedRangeBucketDictionary<T>(keyMin, keyMax, bucketLength);
    }

    /// <summary>Tells the tracker to note this newly-rented or updated segment.</summary>
    /// <param name="segment">The added or updated memory segment.</param>
    /// <param name="tag">An item to associate with this segment, for later retrieval.</param>
    public unsafe void RegisterUpdate<TSegment>(ISegment<TSegment> segment, T tag) where TSegment : unmanaged
    {
        _dict.Add((long)segment.PElems, segment.Length, tag);
    }

    /// <summary>Tells the tracker to note this newly-removed segment.</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    public unsafe void RegisterRemoval<TSegment>(ISegment<TSegment> segment) where TSegment : unmanaged
    {
        _dict.Remove((long)segment.PElems, out _);
    }

    /// <summary>Gets the tag associated with the given segment</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    /// <param name="value">The tag given to the segment, if found.</param>
    /// <returns>True if found.</returns>
    public unsafe bool TryGetTag<TSegment>(ISegment<TSegment> segment, out T value) where TSegment : unmanaged
    {
        if(_dict.TryGetValue((long)segment.PBytes, out var entry) == false)
        {
            value = default!;
            return false;
        }

        value = entry.Value;
        return true;
    }

    /// <summary>Searches the collection for segments that are fragmented, and returns their tags, unordered.</summary>
    /// <param name="minimumFragmentationPct">The fragmentation threshold at which segments are deemed fragmented.</param>
    /// <returns>The tags associated with the segments that are found to be fragmented.</returns>
    public IEnumerable<T> FindFragmentedSegments(double minimumFragmentationPct)
    {
        var enm = _dict.GetBuckets().GetEnumerator();

        if(enm.MoveNext() == false)
        {
            yield break;
        }

        var prevBucket = enm.Current;

        while(enm.MoveNext())
        {
            if(enm.Current.FillPct < minimumFragmentationPct && prevBucket.FillPct >= minimumFragmentationPct)
            {
                foreach (var entry in prevBucket)
                {
                    yield return entry.Value;
                }

                foreach (var entry in enm.Current)
                {
                    yield return entry.Value;
                }

                if(enm.MoveNext() == false)
                {
                    yield break;
                }
            }

            prevBucket = enm.Current;
        }
    }

    /// <summary>Clears all registered segments from the tracker.</summary>
    public void Clear()
    {
        _dict.Clear();
    }
}
