﻿using Suballocation.Collections;

namespace Suballocation.Trackers;

public unsafe class FragmentationTracker<TSeg> : FragmentationTracker<TSeg, EmptyStruct> where TSeg : unmanaged
{
    public FragmentationTracker(long length, long bucketLength) : base(length, bucketLength) { }
}

/// <summary>
/// Provides the ability to approximately track fragmented segments in a buffer.
/// </summary>
/// <typeparam name="TSeg">A blittable element type that defines the units to allocate.</typeparam>
/// <typeparam name="TTag">Type to be tied to each segment, as a separate entity from the segment contents. Use 'EmptyStruct' if none is desired.</typeparam>
public class FragmentationTracker<TSeg, TTag> where TSeg : unmanaged
{ 
    private readonly OrderedRangeBucketDictionary<NativeMemorySegment<TSeg, TTag>> _dict;

    /// <summary></summary>
    /// <param name="length">The unit length of the unmanaged buffer that you are tracking segments for.</param>
    /// <param name="bucketLength">The unit length that each backing bucket is intended to manage. Smaller buckets may improve ordered-lookup performance for non-sparse elements at the cost of GC overhead and memory.</param>
    public FragmentationTracker(long length, long bucketLength)
    {
        _dict = new OrderedRangeBucketDictionary<NativeMemorySegment<TSeg, TTag>>(0, length - 1, bucketLength);
    }

    /// <summary>Tells the tracker to note this newly-rented segment.</summary>
    /// <param name="segment">The added memory segment.</param>
    /// <param name="tag">An item to associate with this segment, for later retrieval.</param>
    public void TrackAddition(NativeMemorySegment<TSeg, TTag> segment)
    {
        _dict.Add(segment);
    }

    /// <summary>Tells the tracker to note this added/updated segment.</summary>
    /// <param name="segment">The new/updated memory segment.</param>
    /// <param name="tag">An item to associate with this segment, for later retrieval.</param>
    public void TrackAdditionOrUpdate(NativeMemorySegment<TSeg, TTag> segment)
    {
        _dict[segment.RangeOffset] = segment;
    }

    /// <summary>Tells the tracker to note this newly-removed segment.</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    public void TrackRemoval(NativeMemorySegment<TSeg, TTag> segment)
    {
        if (_dict.Remove(segment.RangeOffset, out _) == false)
        {
            throw new KeyNotFoundException();
        }
    }

    /*/// <summary>Gets the segment with the given offset.</summary>
    /// <param name="offset">The unit offset of the segment.</param>
    /// <param name="segment">The segment, if found.</param>
    /// <returns>True if found.</returns>
    public bool TryGetSegment(long offset, out NativeMemorySegment<TSeg, TTag> segment)
    {
        return _dict.TryGetValue(offset, out segment);
    }*/

    /// <summary>Searches the collection for segments that are fragmented, and returns them, unordered.</summary>
    /// <param name="minimumFragmentationPct">The fragmentation threshold at which segments are deemed fragmented.</param>
    /// <returns>The segments that are found to be fragmented.</returns>
    public IEnumerable<NativeMemorySegment<TSeg, TTag>> GetFragmentedSegments(double minimumFragmentationPct)
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

    /// <summary>Clears all registered segments from the tracker.</summary>
    public void Clear()
    {
        _dict.Clear();
    }
}
