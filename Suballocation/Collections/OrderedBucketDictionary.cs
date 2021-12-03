
namespace Suballocation.Collections;

/// <summary>
/// A dictionary that provides ordered lookups, in addition to typical dictionary functions in constant time.
/// </summary>
/// <typeparam name="T">The type of the element to store.</typeparam>
public class OrderedBucketDictionary<T> : IEnumerable<KeyValuePair<long, T>> where T : unmanaged
{
    private readonly long _keyMin;
    private readonly long _keyMax;
    private readonly long _keyRange;
    private readonly Dictionary<long, T>[] _buckets;

    /// <summary></summary>
    /// <param name="keyMin">The minimum key value to allow in the collection. The key range dictates the size of a backing array; thus a smaller range is better.</param>
    /// <param name="keyMax">The maximum key value to allow in the collection, inclusive. The key range dictates the size of a backing array; thus a smaller range is better.</param>
    /// <param name="bucketLength">The key-range length that each backing bucket is intended to manage. Smaller buckets may improve ordered-lookup performance for non-sparse elements at the cost of GC overhead and memory.</param>
    public OrderedBucketDictionary(long keyMin, long keyMax, long bucketLength)
    {
        _keyMin = keyMin;
        _keyMax = keyMax;

        _keyRange = keyMax - keyMin + 1;

        long bucketCount = _keyRange / bucketLength;
        if (bucketCount * bucketLength != _keyRange)
        {
            bucketCount++;
        }

        _buckets = new Dictionary<long, T>[bucketCount];

        for (int i = 0; i < _buckets.Length; i++)
        {
            _buckets[i] = new Dictionary<long, T>();
        }
    }

    /// <summary>Returns the count of elements in the collection.</summary>
    public long Count { get; private set; }

    /// <summary>Gets or sets an element in the collection.</summary>
    /// <param name="key">The key of the subject element.</param>
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
            if (key < _keyMin || key > _keyMax) throw new ArgumentOutOfRangeException(nameof(key));

            int bucketIndex = (int)((key - _keyMin) * _buckets.Length / _keyRange);

            _buckets[bucketIndex][key] = value;
        }
    }

    /// <summary>Attempts to retrieve an entry from the collection.</summary>
    /// <param name="key">The key of the desired element.</param>
    /// <param name="value">The value associated with the given key.</param>
    /// <returns>True if found.</returns>
    public bool TryGetValue(long key, out T value)
    {
        if (key < _keyMin || key > _keyMax)
        {
            value = default;
            return false;
        }

        int bucketIndex = (int)((key - _keyMin) * _buckets.Length / _keyRange);

        var bucket = _buckets[bucketIndex];

        return bucket.TryGetValue(key, out value);
    }

    /// <summary>Adds a new element to the collection, if no other element with the range's key exists.</summary>
    /// <param name="key">The key of the element.</param>
    /// <param name="value">The value to store.</param>
    public void Add(long key, T value)
    {
        if (TryAdd(key, value) == false)
        {
            throw new ArgumentException($"Key already exists in the collection.");
        }
    }

    /// <summary>Adds a new element to the collection, if no other element with the range's key exists.</summary>
    /// <param name="key">The key of the element.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>True if successful.</returns>
    public bool TryAdd(long key, T value)
    {
        if (key < _keyMin || key > _keyMax) throw new ArgumentOutOfRangeException(nameof(key));

        int bucketIndex = (int)((key - _keyMin) * _buckets.Length / _keyRange);

        var bucket = _buckets[bucketIndex];

        if (bucket.TryAdd(key, value))
        {
            Count++;
            return true;
        }

        return false;
    }

    /// <summary>Removes an element from the collection.</summary>
    /// <param name="key">The key of the element.</param>
    /// <param name="value">The entry that was removed, if found.</param>
    /// <returns>True if successful.</returns>
    public bool Remove(long key, out T value)
    {
        int bucketIndex = (int)((key - _keyMin) * _buckets.Length / _keyRange);

        var bucket = _buckets[bucketIndex];

        if (bucket.Remove(key, out value))
        {
            Count--;
            return true;
        }

        return false;
    }

    /// <summary>Returns the nearest elements in order.</summary>
    /// <param name="key">The key of the search start location.</param>
    /// <returns>The nearest elements, in order.</returns>
    public IEnumerable<KeyValuePair<long, T>> GetNearest(long key)
    {
        var lowerEnum = GetNearestGreaterOrEqual(key).GetEnumerator();
        var higherEnum = GetNearestLessOrEqual(key).GetEnumerator();

        bool movedLower = lowerEnum.MoveNext();
        bool movedHigher = higherEnum.MoveNext();

        // The first element might be identical when iterating both directions.
        if (movedLower && movedHigher && lowerEnum.Current.Key == higherEnum.Current.Key)
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
            else if (Math.Abs(key - lowerEnum.Current.Key) < Math.Abs(key - higherEnum.Current.Key))
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

    /// <summary>Returns the nearest elements in order, starting from the key upward.</summary>
    /// <param name="key">The key of the search start location.</param>
    /// <returns>The nearest elements, in order.</returns>
    public IEnumerable<KeyValuePair<long, T>> GetNearestGreaterOrEqual(long key)
    {
        key = Math.Min(_keyMax, Math.Max(_keyMin, key));

        int bucketIndex = (int)((key - _keyMin) * _buckets.Length / _keyRange);

        for (int i = bucketIndex; i < _buckets.Length; i++)
        {
            var bucket = _buckets[i];

            foreach (var kvp in bucket.Where(kvp => kvp.Key >= key).OrderBy(kvp => kvp.Key))
            {
                yield return kvp;
            }
        }
    }

    /// <summary>Returns the nearest elements in order, starting from the key downward.</summary>
    /// <param name="key">The key of the search start location.</param>
    /// <returns>The nearest elements, in order.</returns>
    public IEnumerable<KeyValuePair<long, T>> GetNearestLessOrEqual(long key)
    {
        key = Math.Min(_keyMax, Math.Max(_keyMin, key));

        int bucketIndex = (int)((key - _keyMin) * _buckets.Length / _keyRange);

        for (int i = bucketIndex; i >= 0; i--)
        {
            var bucket = _buckets[i];

            foreach (var kvp in bucket.Where(kvp => kvp.Key <= key).OrderByDescending(kvp => kvp.Key))
            {
                yield return kvp;
            }
        }
    }

    /// <summary>Returns the elements in the specified key range, in order.</summary>
    /// <param name="keyLowerBound">The key of the search start location.</param>
    /// <param name="keyUpperBound">The key of the search end location, inclusive.</param>
    /// <returns>The elements, in order.</returns>
    public IEnumerable<KeyValuePair<long, T>> GetRange(long keyLowerBound, long keyUpperBound)
    {
        foreach (var kvp in GetNearestGreaterOrEqual(keyLowerBound))
        {
            if (kvp.Key > keyUpperBound)
            {
                yield break;
            }

            yield return kvp;
        }
    }

    /// <summary>Returns all of the elements in the collection, in key order.</summary>
    public IEnumerator<KeyValuePair<long, T>> GetEnumerator() =>
        GetRange(_keyMin, _keyMax).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns all of the elements in the collection, unordered for best performance.</summary>
    public IEnumerable<KeyValuePair<long, T>> GetUnordered()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            foreach (var entry in _buckets[i])
            {
                yield return entry;
            }
        }
    }

    public void Clear()
    {
        Count = 0;

        for (int i = 0; i < _buckets.Length; i++)
        {
            _buckets[i].Clear();
        }
    }
}
