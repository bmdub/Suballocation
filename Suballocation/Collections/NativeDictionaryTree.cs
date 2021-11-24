using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation.Collections;

internal unsafe class NativeDictionaryTree<TValue> : IDisposable
{
    private readonly long _elementsPerTable;
    private readonly long _tableSize;
    private readonly long _keyRangeStart;
    private readonly long _keyRangeLength;
    private readonly NativeStack<long> _freeStack;
    private long _rootTableOffset;
    private byte* _pElems;
    private long _bufferSizeBytes;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct TableHeader
    {
        private readonly long _valueCount;

        public long ValueCount { get => _valueCount; init => _valueCount = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct Entry
    {
        private readonly byte _header;
        private readonly long _key;
        private readonly TValue _value;

        public bool HasValue { get => (_header & 0b00000001) != 0; init => _header = (byte)((byte)(_header & 0b11111110) | (value ? 1 : 0)); }
        public bool HasTable { get => (_header & 0b00000010) != 0; init => _header = (byte)((byte)(_header & 0b11111101) | (value ? 0x10 : 0)); }
        public long TableOffset { get => _key; init => _key = value; }
        public long Key { get => _key; init => _key = value; }
        public TValue Value { get => _value; init => _value = value; }
    }

    public NativeDictionaryTree(long initialCapacity = 4, long elementsPerTable = 16)
    {
        _elementsPerTable = elementsPerTable;

        long tableCount = initialCapacity / elementsPerTable;
        if (tableCount < initialCapacity)
        {
            tableCount++;
        }

        _tableSize = Unsafe.SizeOf<TableHeader>() + elementsPerTable;

        _bufferSizeBytes = tableCount * _tableSize;

        _pElems = (byte*)NativeMemory.Alloc((nuint)_bufferSizeBytes, 1);

        _freeStack = new NativeStack<long>(tableCount);
        for (long i = 0; i < _bufferSizeBytes; i += _tableSize)
        {
            _freeStack.Push(i);
        }
    }

    public long Count { get; private set; }

    public void Add(long key, TValue value)
    {
        if (TryAdd(key, value) == false)
        {
            throw new ArgumentException($"Key already exists in the collection: {key}");
        }
    }

    public bool TryAdd(long key, TValue value)
    {
        return TryAdd(_rootTableOffset, _keyRangeStart, _keyRangeLength, key, value);
    }

    private bool TryAdd(long tableOffset, long keyRangeStart, long keyRangeLength, long key, TValue value)
    {
        while (key < keyRangeStart || key >= keyRangeStart + keyRangeLength)
        {
            var parentTableOffset = GetFreeTableLocation();

            _rootTableOffset = parentTableOffset;

            keyRangeLength *= _elementsPerTable;

            long prevKeyRangeStart = keyRangeStart;

            if (key < keyRangeStart)
            {
                keyRangeStart -= keyRangeLength + 1;
            }
            else
            {
                keyRangeStart += keyRangeLength - 1;
            }

            ref TableHeader tableHeader = ref Unsafe.AsRef<TableHeader>((void*)_pElems[parentTableOffset]);

            tableHeader = new TableHeader() { ValueCount = 1 };

            // Put the current table as en entry in the new parent table
            long childEntryOffset = (prevKeyRangeStart - keyRangeStart) / keyRangeLength;

            long childElemOffset = parentTableOffset + Unsafe.SizeOf<TableHeader>() + childEntryOffset * Unsafe.SizeOf<Entry>();

            ref Entry childEntry = ref Unsafe.AsRef<Entry>((void*)_pElems[childElemOffset]);

            childEntry = new Entry() { HasTable = true, TableOffset = tableOffset };

            tableOffset = parentTableOffset;
        }

        long entryOffset = (key - keyRangeStart) / keyRangeLength;

        long elemOffset = tableOffset + Unsafe.SizeOf<TableHeader>() + entryOffset * Unsafe.SizeOf<Entry>();

        ref Entry entry = ref Unsafe.AsRef<Entry>((void*)_pElems[elemOffset]);

        if (entry.HasValue == false)
        {
            if (entry.HasTable == false)
            {
                if (entry.Key == key)
                {
                    Count++;
                    return false;
                }

                entry = new Entry() { HasValue = true, Key = key, Value = value };

                ref TableHeader tableHeader = ref Unsafe.AsRef<TableHeader>((void*)_pElems[tableOffset]);

                tableHeader = tableHeader with { ValueCount = tableHeader.ValueCount + 1 };

                return true;
            }
            else
            {
                var childOffset = tableOffset + entry.TableOffset;

                return TryAdd(childOffset, entryOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable, key, value);
            }
        }
        else
        {
            var childOffset = GetFreeTableLocation();

            return TryAdd(childOffset, entryOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable, key, value);
        }
    }

    public TValue this[long key]
    {
        get
        {
            if (TryGetValue(key, out var value) == false)
            {
                throw new KeyNotFoundException();
            }

            return value;
        }
        set
        {
            long entryOffset = GetKeyOffset(key);

            if (entryOffset == -1)
            {
                Add(key, value);
            }
            else
            {
                ref Entry entry = ref Unsafe.AsRef<Entry>((void*)_pElems[entryOffset]);

                entry = entry with { Value = value };
            }
        }
    }

    public bool TryGetValue(long key, out TValue value)
    {
        long entryOffset = GetKeyOffset(key);

        if (entryOffset == -1)
        {
            value = default!;
            return false;
        }

        ref Entry entry = ref Unsafe.AsRef<Entry>((void*)_pElems[entryOffset]);

        value = entry.Value;

        return true;
    }

    public long GetKeyOffset(long key)
    {
        return GetKeyOffset(_rootTableOffset, _keyRangeStart, _keyRangeLength, key);
    }

    public long GetKeyOffset(long tableOffset, long keyRangeStart, long keyRangeLength, long key)
    {
        if (key < keyRangeStart || key >= keyRangeStart + keyRangeLength)
        {
            return -1;
        }

        long entryOffset = (key - keyRangeStart) / keyRangeLength;

        long elemOffset = tableOffset + Unsafe.SizeOf<TableHeader>() + entryOffset * Unsafe.SizeOf<Entry>();

        ref Entry entry = ref Unsafe.AsRef<Entry>((void*)_pElems[elemOffset]);

        if (entry.HasTable == false)
        {
            return elemOffset;
        }
        else
        {
            return GetKeyOffset(entry.TableOffset, entryOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable, key);
        }
    }

    public bool TryRemove(long key, out TValue value)
    {
        long countBefore = Count;

        TryRemove(_rootTableOffset, _keyRangeStart, _keyRangeLength, key, out value);

        return Count != countBefore;
    }

    public long TryRemove(long tableOffset, long keyRangeStart, long keyRangeLength, long key, out TValue value)
    {
        if (key < keyRangeStart || key >= keyRangeStart + keyRangeLength)
        {
            value = default!;
            return -1;
        }

        ref TableHeader tableHeader = ref Unsafe.AsRef<TableHeader>((void*)_pElems[tableOffset]);

        long entryOffset = (key - keyRangeStart) / keyRangeLength;

        long elemOffset = tableOffset + Unsafe.SizeOf<TableHeader>() + entryOffset * Unsafe.SizeOf<Entry>();

        ref Entry entry = ref Unsafe.AsRef<Entry>((void*)_pElems[elemOffset]);

        if (entry.HasTable == false)
        {
            if (entry.HasValue == false)
            {
                value = default!;
                return -1;
            }

            value = entry.Value;

            tableHeader = tableHeader with { ValueCount = tableHeader.ValueCount - 1 };

            Count--;
        }
        else
        {
            long tableCount = TryRemove(entry.TableOffset, entryOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable, key, out value);

            if (tableCount == -1)
            {
                return -1;
            }

            if (tableCount == 0)
            {
                tableHeader = tableHeader with { ValueCount = tableHeader.ValueCount - 1 };

                entry = new Entry();
            }
        }

        return tableHeader.ValueCount;
    }

    public IEnumerable<(long Key, TValue Value)> GetNearest(long key)
    {
        var enm1 = GetAllTo(key, _rootTableOffset, _keyRangeStart, _keyRangeLength).GetEnumerator();
        var enm2 = GetAllFrom(key, _rootTableOffset, _keyRangeStart, _keyRangeLength).GetEnumerator();

        bool moved1 = enm1.MoveNext();
        bool moved2 = enm2.MoveNext();

        for(; ;)
        {
            if(moved1 == false)
            {
                while(moved2 == true)
                {
                    yield return enm2.Current;

                    moved2 = enm2.MoveNext();
                }

                yield break;
            }
            else if (moved2 == false)
            {
                while (moved1 == true)
                {
                    yield return enm1.Current;

                    moved1 = enm1.MoveNext();
                }

                yield break;
            }
            else
            {
                if(key - enm1.Current.Key < enm2.Current.Key - key)
                {
                    yield return (enm1.Current.Key, enm1.Current.Value);

                    moved1 = enm1.MoveNext();
                }
                else
                {
                    yield return (enm2.Current.Key, enm2.Current.Value);

                    moved2 = enm2.MoveNext();
                }
            }
        }
    }

    public IEnumerable<(long Key, TValue Value)> GetAllTo(long key)
    {
        return GetAllTo(key, _rootTableOffset, _keyRangeStart, _keyRangeLength);
    }

    private IEnumerable<(long Key, TValue Value)> GetAllTo(long key, long tableOffset, long keyRangeStart, long keyRangeLength)
    {
        keyRangeStart = Math.Max(_keyRangeStart, keyRangeStart);
        keyRangeLength = Math.Max(_keyRangeLength, keyRangeLength);

        long entryOffset = (key - keyRangeStart) / keyRangeLength;

        for (long i = 0; i <= entryOffset; i++)
        {
            long elemOffset = tableOffset + Unsafe.SizeOf<TableHeader>() + i * Unsafe.SizeOf<Entry>();

            Entry entry = GetEntry(elemOffset);

            if (entry.HasValue)
            {
                if (entry.Key >= keyRangeStart && entry.Key < keyRangeStart + keyRangeLength)
                {
                    yield return (entry.Key, entry.Value);
                }
            }
            else if (entry.HasTable)
            {
                foreach (var pair in GetRange(entry.TableOffset, elemOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable))
                {
                    yield return pair;
                }
            }
        }
    }

    public IEnumerable<(long Key, TValue Value)> GetAllFrom(long key)
    {
        return GetAllFrom(key, _rootTableOffset, _keyRangeStart, _keyRangeLength);
    }

    private IEnumerable<(long Key, TValue Value)> GetAllFrom(long key, long tableOffset, long keyRangeStart, long keyRangeLength)
    {
        keyRangeStart = Math.Max(_keyRangeStart, keyRangeStart);
        keyRangeLength = Math.Max(_keyRangeLength, keyRangeLength);

        long entryOffset = (key - keyRangeStart) / keyRangeLength;

        for (long i = entryOffset; i < _elementsPerTable; i++)
        {
            long elemOffset = tableOffset + Unsafe.SizeOf<TableHeader>() + i * Unsafe.SizeOf<Entry>();

            Entry entry = GetEntry(elemOffset);

            if (entry.HasValue)
            {
                if (entry.Key >= keyRangeStart && entry.Key < keyRangeStart + keyRangeLength)
                {
                    yield return (entry.Key, entry.Value);
                }
            }
            else if (entry.HasTable)
            {
                foreach (var pair in GetRange(entry.TableOffset, elemOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable))
                {
                    yield return pair;
                }
            }
        }
    }

    public IEnumerable<(long Key, TValue Value)> GetRange(long tableOffset) =>
        GetRange(tableOffset, _keyRangeStart, _keyRangeLength);

    private IEnumerable<(long Key, TValue Value)> GetRange(long tableOffset, long keyRangeStart, long keyRangeLength)
    {
        keyRangeStart = Math.Max(_keyRangeStart, keyRangeStart);
        keyRangeLength = Math.Max(_keyRangeLength, keyRangeLength);

        for (long i = 0; i < _elementsPerTable; i++)
        {
            long elemOffset = tableOffset + Unsafe.SizeOf<TableHeader>() + i * Unsafe.SizeOf<Entry>();

            Entry entry = GetEntry(elemOffset);

            if (entry.HasValue)
            {
                if (entry.Key >= keyRangeStart && entry.Key < keyRangeStart + keyRangeLength)
                {
                    yield return (entry.Key, entry.Value);
                }
            }
            else if (entry.HasTable)
            {
                foreach (var pair in GetRange(entry.TableOffset, elemOffset * (keyRangeLength / _elementsPerTable), keyRangeLength / _elementsPerTable))
                {
                    yield return pair;
                }
            }
        }
    }

    private ref Entry GetEntry(long elemOffset) =>
        ref Unsafe.AsRef<Entry>((void*)_pElems[elemOffset]);

    private long GetFreeTableLocation()
    {
        if (_freeStack.Length == 0)
        {
            var pElemsNew = (byte*)NativeMemory.Alloc((nuint)_bufferSizeBytes << 1, 1);
            Buffer.MemoryCopy(_pElems, pElemsNew, _bufferSizeBytes * Unsafe.SizeOf<TValue>(), _bufferSizeBytes * Unsafe.SizeOf<TValue>());
            NativeMemory.Free(_pElems);
            _pElems = pElemsNew;

            for (long i = 0; i < _bufferSizeBytes; i += _tableSize)
            {
                _freeStack.Push(_bufferSizeBytes + i);
            }

            _bufferSizeBytes <<= 1;
        }

        return _freeStack.Pop();
    }

    public void Clear()
    {
        _freeStack.Clear();

        for (long i = 0; i < _bufferSizeBytes; i += _tableSize)
        {
            _freeStack.Push(i);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            NativeMemory.Free(_pElems);
        }

        _disposed = true;
    }

    ~NativeDictionaryTree()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
