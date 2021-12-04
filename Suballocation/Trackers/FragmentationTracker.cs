using Suballocation.Collections;

namespace Suballocation.Trackers;

/// <summary>
/// Provides the ability to approximately track fragmented segments in a buffer.
/// </summary>
/// <typeparam name="TTag">An item type to map to each segment, for later retrieval.</typeparam>
public unsafe class FragmentationTracker<TBuffer, TTag> where TBuffer : unmanaged
{
    private readonly TBuffer* _pBuffer;
    private readonly OrderedRangeBucketDictionary<TTag> _dict;

    /// <summary></summary>
    /// <param name="pBuffer">The address of the unmanaged buffer that you are tracking segments for.</param>
    /// <param name="length">The unit length of the unmanaged buffer that you are tracking segments for.</param>
    /// <param name="bucketLength">The unit length that each backing bucket is intended to manage. Smaller buckets may improve ordered-lookup performance for non-sparse elements at the cost of GC overhead and memory.</param>
    public unsafe FragmentationTracker(TBuffer* pBuffer, long length, long bucketLength)
    {
        _pBuffer = pBuffer;
        _dict = new OrderedRangeBucketDictionary<TTag>(0, length - 1, bucketLength);
    }

    /// <summary>Tells the tracker to note this newly-rented segment.</summary>
    /// <param name="segment">The added memory segment.</param>
    /// <param name="tag">An item to associate with this segment, for later retrieval.</param>
    public unsafe void TrackAdd(ISegment<TBuffer> segment, TTag tag)
    {
        _dict.Add(segment.PElems - _pBuffer, segment.Length, tag);
    }

    /// <summary>Tells the tracker to note this added/updated segment.</summary>
    /// <param name="segment">The new/updated memory segment.</param>
    /// <param name="tag">An item to associate with this segment, for later retrieval.</param>
    public unsafe void TrackAddOrUpdate(ISegment<TBuffer> segment, TTag tag)
    {
        long index = segment.PElems - _pBuffer;

        _dict[index] = new OrderedRangeBucketDictionary<TTag>.RangeEntry(index, segment.Length, tag);
    }

    /// <summary>Tells the tracker to note this newly-removed segment.</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    public unsafe TTag TrackRemoval(ISegment<TBuffer> segment)
    {
        if(_dict.Remove(segment.PElems - _pBuffer, out var tag) == false)
        {
            throw new KeyNotFoundException();
        }

        return tag.Value;
    }

    /// <summary>Gets the tag associated with the given segment</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    /// <param name="value">The tag given to the segment, if found.</param>
    /// <returns>True if found.</returns>
    public unsafe bool TryGetTag(ISegment<TBuffer> segment, out TTag value)
    {
        if(_dict.TryGetValue(segment.PElems - _pBuffer, out var entry) == false)
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
    public IEnumerable<TTag> GetFragmentedSegments(double minimumFragmentationPct)
    {
        var enm = _dict.GetBuckets().GetEnumerator();

        if(enm.MoveNext() == false)
        {
            yield break;
        }

        var prevBucket = enm.Current;

        while(enm.MoveNext())
        {
            if(enm.Current.FillPct > 0 &&
                prevBucket.FillPct > 0 &&
                1.0 - enm.Current.FillPct >= minimumFragmentationPct && 
                1.0 - prevBucket.FillPct >= minimumFragmentationPct)
            {
                foreach (var entry in prevBucket.GetOriginatingRanges())
                {
                    yield return entry.Value;
                }

                foreach (var entry in enm.Current.GetOriginatingRanges())
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
