namespace Suballocation.Collections;

public partial class OrderedRangeBucketDictionary<T>
{
    public readonly record struct RangeEntry
    {
        private readonly long _key;
        private readonly long _length;
        private readonly T _value;

        public long Key { get => _key; init => _key = value; }
        public long Length { get => _length; init => _length = value; }
        public T Value { get => _value; init => _value = value; }

        public RangeEntry(long key, long length, T value) =>
            (_key, _length, _value) = (key, length, value);
    }
}
