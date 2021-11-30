using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suballocation.Collections
{
    public class DictionaryTree<T>
    {
        private DictionaryTree<T>?[]? _branches;
        private Leaf[]? _leaves;

        public DictionaryTree(long keyLowerBound, long keyUpperBound, long maxDictionaryNodeLength = 32)
        {
            KeyLowerBound = keyLowerBound;
            KeyUpperBound = keyUpperBound;
            MaxDictionaryNodeLength = maxDictionaryNodeLength;


        }

        public long KeyLowerBound { get; init; }
        public long KeyUpperBound { get; init; }
        public long MaxDictionaryNodeLength { get; init; }
        public long Count { get; private set; }

        public T this[long key]
        {
            get
            {
                if(TryGetValue(key, out var value) == false)
                {
                    throw new ArgumentException($"Key does not exist in the collection.");
                }

                return value;
            }
            set
            {
                Add(key, value);
            }
        }

        public bool TryGetValue(long key, out T value)
        {
            if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

            long dictRange = KeyUpperBound - KeyLowerBound + 1;
            long entryRange = dictRange / MaxDictionaryNodeLength;
            if (entryRange * MaxDictionaryNodeLength != dictRange)
            {
                entryRange++;
            }

            long index = key / entryRange;

            if (dictRange <= MaxDictionaryNodeLength)
            {
                if (_leaves == null || _leaves[index].Exists == false)
                {
                    value = default!;
                    return false;
                }

                value = _leaves[index].Value;
                return true;
            }

            if (_branches == null || _branches[index] == null)
            {
                value = default!;
                return false;
            }

            return _branches[index]!.TryGetValue(key, out value);
        }

        public void Add(long key, T value)
        {
            if(TryAdd(key, value) == false)
            {
                throw new ArgumentException($"Key already exists in the collection.");
            }            
        }

        public bool TryAdd(long key, T value)
        {
            if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

            long dictRange = KeyUpperBound - KeyLowerBound + 1;
            long entryRange = dictRange / MaxDictionaryNodeLength;
            if (entryRange * MaxDictionaryNodeLength != dictRange)
            {
                entryRange++;
            }

            long index = key / entryRange;

            if (dictRange <= MaxDictionaryNodeLength)
            {
                if(_leaves == null)
                {
                    _leaves = new Leaf[MaxDictionaryNodeLength];
                }
                else
                {
                    if(_leaves[index].Exists == true)
                    {
                        return false;
                    }
                }

                Count++;
                _leaves[index] = new Leaf() { Exists = true, Value = value };
                return true;
            }

            if (_branches == null)
            {
                _branches = new DictionaryTree<T>[MaxDictionaryNodeLength];
            }

            if (_branches[index] == null)
            {
                long childKeyLowerBound = KeyLowerBound + index * entryRange;
                _branches[index] = new DictionaryTree<T>(childKeyLowerBound, childKeyLowerBound + entryRange - 1, MaxDictionaryNodeLength);
            }

            if(_branches[index]!.TryAdd(key, value) == false)
            {
                return false;
            }

            Count++;
            return true;
        }

        public bool Remove(long key, out T value)
        {
            if (key < KeyLowerBound || key > KeyUpperBound) throw new ArgumentOutOfRangeException(nameof(key));

            long dictRange = KeyUpperBound - KeyLowerBound + 1;
            long entryRange = dictRange / MaxDictionaryNodeLength;
            if (entryRange * MaxDictionaryNodeLength != dictRange)
            {
                entryRange++;
            }

            long index = key / entryRange;

            if (dictRange <= MaxDictionaryNodeLength)
            {
                if (_leaves == null || _leaves[index].Exists == false)
                {
                    value = default!;
                    return false;
                }

                Count--;
                _leaves[index] = new Leaf() { Exists = false };
                value = _leaves[index].Value;

                if(Count == 0)
                {
                    _leaves = null;
                }

                return true;
            }

            if (_branches == null || _branches[index] == null)
            {
                value = default!;
                return false;
            }

            if (_branches[index]!.Remove(key, out value) == false)
            {
                value = default!;
                return false;
            }

            if(_branches[index]!.Count == 0)
            {
                _branches[index] = null;
            }

            Count--;
            return true;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly record struct Leaf
        {
            private readonly bool _exists;
            private readonly T _value;

            public bool Exists { get => _exists; init => _exists = value; }
            public T Value { get => _value; init => _value = value; }
        }
    }
}
