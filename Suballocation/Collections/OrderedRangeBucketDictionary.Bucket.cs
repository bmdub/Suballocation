
namespace Suballocation.Collections;

public partial class OrderedRangeBucketDictionary<T>
{
    /// <summary>
    /// Structure used for storing a locally-similar portion of elements.
    /// </summary>
    public record struct Bucket : IEnumerable<T>
    {
        private readonly Dictionary<long, T> _dict;
        private readonly long _minOffset;
        private readonly long _size;
        private long _fill;

        /// <summary></summary>
        /// <param name="minOffset">The minimum offset of the range that this bucket handles.</param>
        /// <param name="size">The length of the key range that this bucket handles.</param>
        public Bucket(long minOffset, long size)
        {
            _dict = new Dictionary<long, T>();
            _minOffset = minOffset;
            _size = size;
            _fill = 0;
        }

        /// <summary>Returns the sum of the elements and their ranges currently in this bucket.</summary>
        public long Fill { get => _fill; init => _fill = value; }

        /// <summary>Returns the sum of the elements and their ranges currently in this bucket, as a percentage of the range.</summary>
        public double FillPct => _fill / (double)_size;

        /// <summary>Returns the count of the elements in this bucket.</summary>
        public long Count => _dict.Count;

        /// <summary>Gets or sets an entry associated with the given starting offset.</summary>
        /// <param name="offset">The offset of the entry.</param>
        /// <returns>The range entry associated with the given offset.</returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public T this[long offset]
        {
            get
            {
                if (TryGetValue(offset, out var entry) == false)
                {
                    throw new KeyNotFoundException($"Range not found.");
                }

                return entry;
            }
            set
            {
                Remove(offset, out _);
                Add(value);
            }
        }

        /// <summary>Attempts to retrieve an entry from the bucket.</summary>
        /// <param name="offset">The offset of the entry.</param>
        /// <param name="entry">The entry associated with the given range offset, if found.</param>
        /// <returns>True if found.</returns>
        public bool TryGetValue(long offset, out T entry)
        {
            return _dict.TryGetValue(offset, out entry!);
        }

        /// <summary>Returns whether or not there exists an entry with the given offset.</summary>
        /// <param name="offset">The offset of the entry.</param>
        /// <returns>True if found.</returns>
        public bool ContainsKey(long offset) => _dict.ContainsKey(offset);

        /// <summary>Adds a new entry to the bucket, if no other entry with the range's offset exists.</summary>
        /// <param name="entry">The entry to add.</param>
        public void Add(T entry)
        {
            if (TryAdd(entry) == false)
            {
                throw new ArgumentException($"Key already exists in the collection.");
            }
        }

        /// <summary>Adds a new entry to the bucket, if no other entry with the range's offset exists.</summary>
        /// <param name="entry">The entry to add.</param>
        /// <returns>True if successful.</returns>
        public bool TryAdd(T entry)
        {
            if (_dict.TryAdd(entry.RangeOffset, entry))
            {
                _fill += entry.RangeLength;
                //Debug.WriteLine($"MinKey {_minKey}, {_fill}");
                return true;
            }

            return false;
        }

        /// <summary>Removes an entry from the bucket.</summary>
        /// <param name="offset">The range offset of the entry.</param>
        /// <param name="entry">The entry that was removed, if found.</param>
        /// <returns>True if successful.</returns>
        public bool Remove(long offset, out T entry)
        {
            if (_dict.Remove(offset, out entry!))
            {
                _fill -= entry.RangeLength;
                return true;
            }

            entry = default!;
            return false;
        }

        /// <summary>Removes all elements from the bucket.</summary>
        public void Clear()
        {
            _fill = 0;
            _dict.Clear();
        }

        /// <summary>Returns all of the elements in the bucket.</summary>
        public IEnumerator<T> GetEnumerator() => _dict.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Returns all of the elements whose key belongs in this bucket range.</summary>
        public IEnumerable<T> GetOriginatingRanges()
        {
            var minOffset = _minOffset;

            return _dict
                    .Where(kvp => kvp.Key >= minOffset)
                    .Select(kvp => kvp.Value);
        }
    }
}