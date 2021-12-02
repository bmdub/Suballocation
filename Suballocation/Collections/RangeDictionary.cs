using System.Collections;
using System.Numerics;

// todo
// space filling curve
// custom dictionary, that allows modifying value in place?
// replace inner dict with some other value-type thing that can grow/shrink.  can get rid of storing key? or just alloc 64 values.  bit field too?

namespace Suballocation.Collections
{
    public class RangeDictionary<T> : IEnumerable<KeyValuePair<long, T>>
    {
        private readonly long _maxLeafCount;
        private readonly ulong[] _treeNodeBits;
        private TreeLevel[] _treeLevels;
        private readonly T  <long, Dictionary<byte, T>> _leaves = new(); //todo: dict struct?

        public RangeDictionary(long keyLowerBound, long keyUpperBound)
        {
            KeyLowerBound = keyLowerBound;
            KeyUpperBound = keyUpperBound;
            KeyRange = keyUpperBound - keyLowerBound + 1;

            _maxLeafCount = KeyRange >> 6;
            byte remainder = (byte)(_maxLeafCount & 63);
            if (remainder != 0)
            {
                _maxLeafCount++;
            }

            long indexLength = 0;
            int levels = 0;

            long leafCount = 1;
            while (leafCount < _maxLeafCount)
            {
                leafCount <<= 6;
                levels++;
            }

            _treeLevels = new TreeLevel[levels];
            long levelLength = 1;
            long levelOffset = 0;

            for (int i = 0; i < levels; i++)
            {
                _treeLevels[i] = new TreeLevel() { Level = i, BinaryOffset = levelOffset, BinaryLength = levelLength };
                indexLength += levelLength;
                levelOffset += levelLength;
                levelLength <<= 6;
            }

            long indexLengthLong = indexLength >> 6;
            if((indexLengthLong << 6) != indexLength)
            {
                indexLengthLong++;
            }

            _treeNodeBits = new ulong[indexLengthLong];
        }

        public long KeyLowerBound { get; init; }
        public long KeyUpperBound { get; init; }
        public long KeyRange { get; init; }
        public long Count { get; private set; }

        public T this[long key]
        {
            get
            {
                if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

                if (TryGetValue(key, out var value) == false)
                {
                    throw new ArgumentException($"Key does not exist in the collection.");
                }

                return value;
            }
            set
            {
                if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

                var leafIndex = (key - KeyLowerBound) >> 6;

                if (_leaves.TryGetValue(leafIndex, out var leaf) == false)
                {
                    Add(key, value);
                    return;
                }

                if (leaf.TryGetValue((byte)(key & 63), out _) == false)
                {
                    Add(key, value);
                    return;
                }

                leaf[(byte)(key & 63)] = value;
            }
        }

        public bool TryGetValue(long key, out T value)
        {
            if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

            var leafIndex = (key - KeyLowerBound) >> 6;

            if (_leaves.TryGetValue(leafIndex, out var leaf) == false)
            {
                value = default!;
                return false;
            }

            if (leaf.TryGetValue((byte)(key & 255), out value!) == false)
            {
                value = default!;
                return false;
            }

            return true;
        }

        public void Add(long key, T value)
        {
            if (TryAdd(key, value) == false)
            {
                throw new ArgumentException($"Key already exists in the collection.");
            }
        }

        public bool TryAdd(long key, T value)
        {
            if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

            long leafIndex = (key - KeyLowerBound) >> 6;

            if (_leaves.TryGetValue(leafIndex, out var leaf) == false)
            {
                leaf = new Dictionary<byte, T>(1);
                _leaves[leafIndex] = leaf;
            }

            if (leaf.TryAdd((byte)(key & 63), value) == false)
            {
                return false;
            }

            long column = leafIndex;

            long binaryIndex = _treeLevels[^1].BinaryOffset + column;

            //if (key == 5500)
            //Debugger.Break();

            for (; ; )
            {
                long longIndex = binaryIndex >> 6;
                byte bitIndex = (byte)(binaryIndex & 63);

                if ((_treeNodeBits[longIndex] & (1UL << bitIndex)) != 0)
                {
                    break;
                }

                _treeNodeBits[longIndex] = _treeNodeBits[longIndex] | (1UL << bitIndex);

                if (binaryIndex == 0)
                {
                    break;
                }

                binaryIndex = (binaryIndex - 1) >> 6;
            }

            Count++;
            return true;
        }

        public bool Remove(long key, out T value)
        {
            if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

            var leafIndex = (key - KeyLowerBound) >> 6;

            if (_leaves.TryGetValue(leafIndex, out var leaf) == false)
            {
                value = default!;
                return false;
            }

            if (leaf.Remove((byte)(key & 63), out value!) == false)
            {
                return false;
            }

            if (leaf.Count == 0)
            {
                _leaves.Remove(leafIndex);

                long column = leafIndex;

                long binaryIndex = _treeLevels[^1].BinaryOffset + column;

                for (; ; )
                {
                    long longIndex = binaryIndex >> 6;
                    byte bitIndex = (byte)(binaryIndex & 63);

                    _treeNodeBits[longIndex] = _treeNodeBits[longIndex] & ~(1UL << bitIndex);

                    if (binaryIndex == 0)
                    {
                        break;
                    }

                    // Siblings must also be empty in order for parents bits to be cleared.
                    if (_treeNodeBits[longIndex] != 0)
                    {
                        break;
                    }

                    binaryIndex = (binaryIndex - 1) >> 6;
                }
            }
            else
            {
                if (BitOperations.IsPow2(leaf.Count))
                {
                    leaf.TrimExcess();
                }
            }

            Count--;
            return true;
        }

        public IEnumerable<KeyValuePair<long, T>> GetNearest(long key)
        {
            var lowerEnum = GetNearestDirectional(key, false).GetEnumerator();
            var higherEnum = GetNearestDirectional(key, true).GetEnumerator();

            bool movedLower = lowerEnum.MoveNext();
            bool movedHigher = higherEnum.MoveNext();

            if (movedLower && movedHigher && lowerEnum.Current.Key == higherEnum.Current.Key)
            {
                yield return lowerEnum.Current;

                movedLower = lowerEnum.MoveNext();
                movedHigher = higherEnum.MoveNext();
            }

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

        public IEnumerable<KeyValuePair<long, T>> GetNearestDirectional(long key, bool higher)
        {
            return TraverseIndex(0, 0);

            IEnumerable<KeyValuePair<long, T>> TraverseIndex(int height, long binaryIndex)
            {
                if (height >= _treeLevels.Length)
                {
                    long column = binaryIndex - (_treeLevels[^1].BinaryOffset + _treeLevels[^1].BinaryLength);

                    long subkey = column;

                    long leafKeyKey = (subkey << 6) + KeyLowerBound;

                    if (_leaves.TryGetValue(subkey, out var leaf) == true)
                    {
                        foreach (var kvp in leaf
                            .Select(kvp => new KeyValuePair<long, T>(kvp.Key | leafKeyKey, kvp.Value))
                            .Where(kvp => higher == false ? kvp.Key <= key : kvp.Key >= key)
                            .OrderBy(kvp => kvp.Key))
                        {
                            yield return new KeyValuePair<long, T>(kvp.Key, kvp.Value);
                        }
                    }
                }
                else
                {
                    long longIndex = binaryIndex >> 6;

                    if (_treeNodeBits[longIndex] == 0)
                    {
                        yield break;
                    }

                    var treeLevel = _treeLevels[height];

                    long column = binaryIndex - treeLevel.BinaryOffset;

                    long subkey = KeyLowerBound + column * KeyRange / treeLevel.BinaryLength;

                    long keyRange = KeyRange >> (height * 6);

                    if (higher)
                    {
                        if (subkey + keyRange < key)
                        {
                            yield break;
                        }
                    }
                    else
                    {
                        if (subkey > key)
                        {
                            yield break;
                        }
                    }

                    long child1BinaryIndex = (binaryIndex << 6) + 1;

                    for (int i = 0; i < 64; i++)
                    {
                        long nextIndex = child1BinaryIndex + i;

                        foreach (var kvp in TraverseIndex(height + 1, nextIndex))
                        {
                            yield return kvp;
                        }
                    }
                }
            }
        }

        public IEnumerable<KeyValuePair<long, T>> GetRange(long keyLowerBound, long keyUpperBound)
        {
            foreach (var kvp in GetNearestDirectional(keyLowerBound, true))
            {
                if (kvp.Key > keyUpperBound)
                {
                    yield break;
                }
                else if (kvp.Key == keyUpperBound)
                {
                    yield return kvp;

                    yield break;
                }
                else
                {
                    yield return kvp;
                }
            }
        }

        public IEnumerator<KeyValuePair<long, T>> GetEnumerator() =>
            GetRange(KeyLowerBound, KeyUpperBound)
            .GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Clear()
        {
            Count = 0;
            _leaves.Clear();
            _treeNodeBits.AsSpan().Clear();
        }

        /*private static ulong RoundUpToPowerOf64(ulong v)
        {
            if (v == 0)
            {
                return 1;
            }

            int firstOne = 63 - BitOperations.LeadingZeroCount(v);

            const ulong powers = 0b00010000_01000001_00000100_00010000_01000001_00000100_00010000_01000001;

            int shift = firstOne + BitOperations.TrailingZeroCount(powers >> firstOne);

            ulong rounded = 1UL << shift;

            return rounded >= v ? rounded : rounded << 6;
        }*/

        private readonly record struct TreeLevel
        {
            private readonly int _level;
            private readonly long _binaryOffset;
            private readonly long _binaryLength;

            public int Level { get => _level; init => _level = value; }
            public long BinaryOffset { get => _binaryOffset; init => _binaryOffset = value; }
            public long BinaryLength { get => _binaryLength; init => _binaryLength = value; }
        }
    }
}
