

namespace Suballocation.Collections;

/// <summary>
/// A dictionary for ranges that provides ordered lookups, and divides the collection into buckets that can later be analyzed.
/// </summary>
/// <typeparam name="T">The element type, which must implement IRangedEntity.</typeparam>
public partial class OrderedRangeBucketDictionary<T> : IEnumerable<T> where T : IRangedEntry
{
    private readonly long _bucketLength;
    private readonly long _offsetMin;
    private readonly long _offsetMax;
    private readonly long _keyRange;
    private readonly Bucket[] _buckets;

    /// <summary></summary>
    /// <param name="offsetMin">The minimum key value to allow in the collection. The key range dictates the size of a backing array; thus a smaller range is better.</param>
    /// <param name="offsetMax">The maximum key value to allow in the collection, inclusive. The key range dictates the size of a backing array; thus a smaller range is better.</param>
    /// <param name="bucketLength">The length of the buckets in which this collection is divided into. In general, smaller buckets = faster ordered range searches; larger buckets = Faster addition/removal, less memory overhead and possibly more ideal bucket statistics.</param>
    public OrderedRangeBucketDictionary(long offsetMin, long offsetMax, long bucketLength)
    {
        _offsetMin = offsetMin;
        _offsetMax = offsetMax;
        _bucketLength = bucketLength;

        _keyRange = offsetMax - offsetMin + 1;

        long bucketCount = _keyRange / bucketLength;
        if (bucketCount * bucketLength != _keyRange)
        {
            bucketCount++;
        }

        _buckets = new Bucket[bucketCount];

        long minKey = _offsetMin;
        for (int i = 0; i < _buckets.Length; i++)
        {
            _buckets[i] = new Bucket(minKey, Math.Min(bucketLength, _offsetMax - minKey + 1));
            minKey += bucketLength;
        }
    }

    /// <summary>Returns the count of elements in the collection.</summary>
    public long Count { get; private set; }

    /// <summary>Gets or sets an element in the collection, given the start of its range.</summary>
    /// <param name="key">The entry's range offset.</param>
    /// <returns>The entry associated with the given key.</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public T this[long key]
    {
        get
        {
            if (TryGetValue(key, out var value) == false)
            {
                throw new ArgumentException($"Key does not exist in the collection.");
            }

            return value;
        }
        set
        {
            Remove(key, out _);

            Add(value);
        }
    }

    /// <summary>Attempts to retrieve an entry from the collection.</summary>
    /// <param name="offset">The offset of the entry.</param>
    /// <param name="entry">The entry associated with the given key.</param>
    /// <returns>True if found.</returns>
    public bool TryGetValue(long offset, out T entry)
    {
        if (offset < _offsetMin || offset > _offsetMax)
        {
            entry = default!;
            return false;
        }

        int bucketIndex = (int)((offset - _offsetMin) / _bucketLength);

        ref Bucket bucket = ref _buckets[bucketIndex];

        return bucket.TryGetValue(offset, out entry);
    }

    /// <summary>Adds a new entry to the collection, if no other entry with the range's key exists.</summary>
    /// <param name="entry">The entry to add.</param>
    public void Add(T entry)
    {
        if (TryAdd(entry) == false)
        {
            throw new ArgumentException($"Key already exists in the collection.");
        }
    }

    /// <summary>Adds a new entry to the collection, if no other entry with the range's key exists.</summary>
    /// <param name="entry">The entry to add.</param>
    /// <returns>True if successful.</returns>
    public bool TryAdd(T entry)
    {
        if (entry.RangeOffset < _offsetMin || entry.RangeOffset > _offsetMax)
            throw new ArgumentOutOfRangeException(nameof(entry.RangeOffset));

        int bucketIndexMin = (int)((entry.RangeOffset - _offsetMin) / _bucketLength);
        int bucketIndexMax = (int)((entry.RangeOffset + entry.RangeLength - 1 - _offsetMin)  / _bucketLength);
        bucketIndexMax = Math.Min(_buckets.Length - 1, bucketIndexMax);

        for (int bucketIndex = bucketIndexMin; bucketIndex <= bucketIndexMax; bucketIndex++)
        {
            ref Bucket bucket = ref _buckets[bucketIndex];

            if (bucket.TryAdd(entry) == false)
            {
                return false;
            }
        }

        Count++;
        return true;
    }

    /// <summary>Removes an entry from the collection.</summary>
    /// <param name="offset">The offset of the entry.</param>
    /// <param name="entry">The entry that was removed, if found.</param>
    /// <returns>True if successful.</returns>
    public bool Remove(long offset, out T entry)
    {
        if (offset < _offsetMin || offset > _offsetMax)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int bucketIndexMin = (int)((offset - _offsetMin) / _bucketLength);

        ref Bucket bucket = ref _buckets[bucketIndexMin];

        if (bucket.Remove(offset, out entry) == false)
        {
            return false;
        }

        bucketIndexMin++;
        int bucketIndexMax = (int)((offset + entry.RangeLength - 1 - _offsetMin) / _bucketLength);
        bucketIndexMax = Math.Min(_buckets.Length - 1, bucketIndexMax);

        for (int bucketIndex = bucketIndexMin; bucketIndex <= bucketIndexMax; bucketIndex++)
        {
            bucket = ref _buckets[bucketIndex];

            bool success = bucket.Remove(offset, out _);

            Debug.Assert(success);
        }

        Count--;
        return true;
    }

    /// <summary>Returns the nearest entries in order.</summary>
    /// <param name="offset">The search start location.</param>
    /// <returns>The nearest entries, in order.</returns>
    public IEnumerable<T> GetNearest(long offset)
    {
        var lowerEnum = GetNearestGreaterOrEqual(offset).GetEnumerator();
        var higherEnum = GetNearestLessOrEqual(offset).GetEnumerator();

        bool movedLower = lowerEnum.MoveNext();
        bool movedHigher = higherEnum.MoveNext();

        // The first element might be identical when iterating both directions.
        if (movedLower && movedHigher && lowerEnum.Current.RangeOffset == higherEnum.Current.RangeOffset)
        {
            yield return lowerEnum.Current;

            movedLower = lowerEnum.MoveNext();
            movedHigher = higherEnum.MoveNext();
        }

        // Return the closest of the two sides/elements.
        for (; ; )
        {
            if (movedLower == false)
            {
                while (movedHigher)
                {
                    yield return higherEnum.Current;

                    movedHigher = higherEnum.MoveNext();
                }

                yield break;
            }
            else if (movedHigher == false)
            {
                while (movedLower)
                {
                    yield return lowerEnum.Current;

                    movedLower = lowerEnum.MoveNext();
                }

                yield break;
            }
            else if (Math.Abs(offset - lowerEnum.Current.RangeOffset) < Math.Abs(offset - higherEnum.Current.RangeOffset))
            {
                yield return lowerEnum.Current;

                movedLower = lowerEnum.MoveNext();
            }
            else
            {
                yield return higherEnum.Current;

                movedHigher = higherEnum.MoveNext();
            }
        }
    }

    /// <summary>Returns the nearest entries in order, starting from the offset upward.</summary>
    /// <param name="offset">The search start location.</param>
    /// <returns>The nearest entries, in order.</returns>
    public IEnumerable<T> GetNearestGreaterOrEqual(long offset)
    {
        offset = Math.Min(_offsetMax, Math.Max(_offsetMin, offset));

        int bucketIndex = (int)((offset - _offsetMin) / _bucketLength);

        var prevBucket = new Bucket(0, 0);

        for (int i = bucketIndex; i < _buckets.Length; i++)
        {
            var bucket = _buckets[i];

            foreach (var kvp in bucket
                .Where(kvp => kvp.RangeOffset >= offset && prevBucket.ContainsKey(kvp.RangeOffset) == false)
                .OrderBy(kvp => kvp.RangeOffset))
            {
                yield return kvp;
            }

            prevBucket = bucket;
        }
    }

    /// <summary>Returns the nearest entries in order, starting from the offset downward.</summary>
    /// <param name="offset">The search start location.</param>
    /// <returns>The nearest entries, in order.</returns>
    public IEnumerable<T> GetNearestLessOrEqual(long offset)
    {
        offset = Math.Min(_offsetMax, Math.Max(_offsetMin, offset));

        int bucketIndex = (int)((offset - _offsetMin) / _bucketLength);

        var prevBucket = new Bucket(0, 0);

        for (int i = bucketIndex; i >= 0; i--)
        {
            var bucket = _buckets[i];

            foreach (var kvp in bucket
                .Where(kvp => kvp.RangeOffset <= offset && prevBucket.ContainsKey(kvp.RangeOffset) == false)
                .OrderByDescending(kvp => kvp.RangeOffset))
            {
                yield return kvp;
            }

            prevBucket = bucket;
        }
    }

    /// <summary>Returns the entries in the specified key range, in order.</summary>
    /// <param name="lowerBound">The search start location.</param>
    /// <param name="lowerBound">The search end location, inclusive.</param>
    /// <returns>The elements, in order.</returns>
    public IEnumerable<T> GetRange(long lowerBound, long upperBound)
    {
        foreach (var kvp in GetNearestGreaterOrEqual(lowerBound))
        {
            if (kvp.RangeOffset > upperBound)
            {
                yield break;
            }

            yield return kvp;
        }
    }

    /// <summary>Returns all of the elements in the collection, in key order.</summary>
    public IEnumerator<T> GetEnumerator() =>
        GetRange(_offsetMin, _offsetMax).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns all of the elements in the collection, unordered for best performance.</summary>
    public IEnumerable<T> GetUnordered()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            foreach(var entry in _buckets[i])
            {
                yield return entry;
            }
        }
    }

    /// <summary>Returns the buckets, in order, used to contain the entries and gather statistics.</summary>
    public IEnumerable<Bucket> GetBuckets() => _buckets;

    /// <summary>Removes all elements from the collection.</summary>
    public void Clear()
    {
        Count = 0;

        for (int i = 0; i < _buckets.Length; i++)
        {
            _buckets[i].Clear();
        }
    }
}
