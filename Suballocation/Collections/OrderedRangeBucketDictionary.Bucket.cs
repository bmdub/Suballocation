
namespace Suballocation.Collections;

public partial class OrderedRangeBucketDictionary<T>
{
    /// <summary>
    /// Structure used for storing a locally-similar portion of elements.
    /// </summary>
    public record struct Bucket : IEnumerable<RangeEntry>
    {
        private readonly Dictionary<long, BucketEntry> _dict;
        private readonly long _minKey;
        private readonly long _size;
        private long _fill;

        /// <summary></summary>
        /// <param name="minKey">The minimum key of the key range that this bucket handles.</param>
        /// <param name="size">The length of the key range that this bucket handles.</param>
        public Bucket(long minKey, long size)
        {
            _dict = new Dictionary<long, BucketEntry>();
            _minKey = minKey;
            _size = size;
            _fill = 0;
        }

        /// <summary>Returns the sum of the elements and their ranges currently in this bucket.</summary>
        public long Fill { get => _fill; init => _fill = value; }

        /// <summary>Returns the sum of the elements and their ranges currently in this bucket, as a percentage of the key range.</summary>
        public double FillPct => _fill / (double)_size;

        /// <summary>Returns the count of the elements in this bucket.</summary>
        public long Count => _dict.Count;

        /// <summary>Gets or sets an entry associated with the given key.</summary>
        /// <param name="key">The key for the element.</param>
        /// <returns>The range entry associated with the given key.</returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public RangeEntry this[long key]
        {
            get
            {
                if (TryGetValue(key, out var entry) == false)
                {
                    throw new KeyNotFoundException($"Key not found.");
                }

                return entry;
            }
            set
            {
                Remove(key, out _);
                TryAdd(value);
            }
        }

        /// <summary>Attempts to retrieve an entry from the bucket.</summary>
        /// <param name="key">The key of the desired element.</param>
        /// <param name="entry">The entry associated with the given key, if found.</param>
        /// <returns>True if found.</returns>
        public bool TryGetValue(long key, out RangeEntry entry)
        {
            if (_dict.TryGetValue(key, out var bucketEntry) == true)
            {
                entry = new RangeEntry() { Key = key, Length = bucketEntry.Length, Value = bucketEntry.Value };
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>Returns whether or not there exists an element with the given key.</summary>
        /// <param name="key">The key of the desired element.</param>
        /// <returns>True if found.</returns>
        public bool ContainsKey(long key) => _dict.ContainsKey(key);

        /// <summary>Adds a new element to the bucket, if no other element with the range's key exists.</summary>
        /// <param name="entry">The range definition of the element.</param>
        public void Add(RangeEntry entry)
        {
            if (TryAdd(entry) == false)
            {
                throw new ArgumentException($"Key already exists in the collection.");
            }
        }

        /// <summary>Adds a new element to the bucket, if no other element with the range's key exists.</summary>
        /// <param name="entry">The range definition of the element.</param>
        /// <returns>True if successful.</returns>
        public bool TryAdd(RangeEntry entry)
        {
            if (_dict.TryAdd(entry.Key, new BucketEntry() { Length = entry.Length, Value = entry.Value }))
            {
                long rangeStart = Math.Max(_minKey, entry.Key);
                long rangeEnd = Math.Min(_minKey + _size, entry.Key + entry.Length);
                _fill += rangeEnd - rangeStart;

                return true;
            }

            return false;
        }

        /// <summary>Removes an element from the bucket.</summary>
        /// <param name="key">The key of the element.</param>
        /// <param name="entry">The entry that was removed, if found.</param>
        /// <returns>True if successful.</returns>
        public bool Remove(long key, out RangeEntry entry)
        {
            if (_dict.Remove(key, out var bucketEntry))
            {
                long rangeStart = Math.Max(_minKey, key);
                long rangeEnd = Math.Min(_minKey + _size, key + bucketEntry.Length);
                _fill -= rangeEnd - rangeStart;

                entry = new RangeEntry() { Key = key, Length = bucketEntry.Length, Value = bucketEntry.Value };
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>Removes all elements from the bucket.</summary>
        public void Clear()
        {
            _fill = 0;
            _dict.Clear();
        }

        /// <summary>Returns all of the elements in the bucket.</summary>
        public IEnumerator<RangeEntry> GetEnumerator() =>
            _dict
            .Select(kvp => new RangeEntry() { Key = kvp.Key, Length = kvp.Value.Length, Value = kvp.Value.Value })
            .GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private readonly record struct BucketEntry
        {
            private readonly long _length;
            private readonly T _value;

            public long Length { get => _length; init => _length = value; }
            public T Value { get => _value; init => _value = value; }
        }
    }
}