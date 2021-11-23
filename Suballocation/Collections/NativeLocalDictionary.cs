using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation.Collections;
/*
internal unsafe class NativeLocalDictionary<TValue> : IDisposable
{
    private readonly long _bucketLength;
    private readonly long _bucketsPerChunk;
    private readonly long _bucketSize;
    private readonly long _chunkSize;
    private readonly long _keyRangeStart;
    private readonly long _keyRangeLength;
    private readonly NativeStack<long> _freeStack;
    private byte* _pElems;
    private long _bufferSizeBytes;
    private bool _disposed;


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ChunkHeader
    {
        private readonly long _valueCount;

        public long ValueCount { get => _valueCount; init => _valueCount = value; }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct BucketHeader
    {
        private readonly ulong _general;
        private readonly long _valueCount;

        public bool HasChild { get => (_general & 1) != 0; init => _general = (_general & 0xFFFFFFFFFFFFFFFE) | (value ? 1 : (ulong)0); }
        public long ChildOffset { get => (long)(_general & 0x7FFFFFFFFFFFFFFF); init => _general = (ulong)(value & 0x7FFFFFFFFFFFFFFF) | (_general & 0x7000000000000000); }
        public long ValueCount { get => _valueCount; init => _valueCount = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct Entry
    {
        private readonly byte _header;
        private readonly long _key;
        private readonly TValue _value;

        public bool HasValue { get => (_header & 0b00000001) != 0; init => _header = (byte)((byte)(_header & 0b11111110) | (value ? 1 : 0)); }
        public long Key { get => _key; init => _key = value; }
        public TValue Value { get => _value; init => _value = value; }
    }

    public NativeLocalDictionary(long keyRangeStart, long keyRangeLength, long initialCapacity = 4, long bucketLength = 1, long bucketsPerChunk = 16)
    {
        _keyRangeStart = keyRangeStart;
        _keyRangeLength = keyRangeLength;
        _bucketLength = bucketLength;
        _bucketsPerChunk = bucketsPerChunk;

        long chunkCount = initialCapacity / bucketLength / bucketsPerChunk;
        if (chunkCount * bucketLength < initialCapacity)
        {
            chunkCount++;
        }

        _bucketSize = Unsafe.SizeOf<BucketHeader>() + bucketLength * Unsafe.SizeOf<Entry>(); //bucketLength * bucketsPerChunk * Unsafe.SizeOf<Entry>();
        _chunkSize = Unsafe.SizeOf<ChunkHeader>() + _bucketSize * bucketsPerChunk;

        _bufferSizeBytes = chunkCount * _chunkSize;

        _pElems = (byte*)NativeMemory.Alloc((nuint)_bufferSizeBytes, 1);

        _freeStack = new NativeStack<long>(chunkCount);
        for (long i = 0; i < _bufferSizeBytes; i += _chunkSize)
        {
            _freeStack.Push(i);
        }
    }

    public long Length { get; private set; }

    public bool TryAdd(long key, TValue value)
    {
        if (key < _keyRangeStart || key >= _keyRangeStart + _keyRangeLength)
            throw new ArgumentOutOfRangeException(nameof(key));

        return TryAdd(_pElems, _keyRangeStart, _keyRangeLength, key, value);
    }

    private bool TryAdd(byte* chunkOffset, long keyRangeStart, long keyRangeLength, long key, TValue value)
    {
        long entryIndex = (key - keyRangeStart) / keyRangeLength;

        long bucketOffset = Unsafe.SizeOf<ChunkHeader>() + _bucketSize * entryIndex;

        ref BucketHeader bucketHeader = ref Unsafe.AsRef<BucketHeader>(chunkOffset + bucketOffset);

        if (bucketHeader.ValueCount < _bucketLength)
        {
            for (int i = 0; i < _bucketLength; i++)
            {
                ref Entry entry = ref Unsafe.AsRef<Entry>((void*)chunkOffset[Unsafe.SizeOf<BucketHeader>() + bucketOffset + i * Unsafe.SizeOf<Entry>()]);

                if (entry.HasValue)
                {
                    if (entry.Key == key)
                    {
                        throw new ArgumentException($"Key already exists in the collection: {key}");
                    }

                    continue;
                }

                entry = new Entry() { HasValue = true, Key = key, Value = value };

                bucketHeader = bucketHeader with { ValueCount = bucketHeader.ValueCount + 1 };

                ref ChunkHeader chunkHeader = ref Unsafe.AsRef<ChunkHeader>(chunkOffset);

                chunkHeader = new ChunkHeader() { ValueCount = chunkHeader.ValueCount + 1 };

                return true;
            }
        }

        byte* childOffset;

        if (bucketHeader.HasChild == false)
        {
            if (_freeStack.Length == 0)
            {
                var pElemsNew = (byte*)NativeMemory.Alloc((nuint)_bufferSizeBytes << 1, 1);
                Buffer.MemoryCopy(_pElems, pElemsNew, _bufferSizeBytes * Unsafe.SizeOf<TValue>(), _bufferSizeBytes * Unsafe.SizeOf<TValue>());
                NativeMemory.Free(_pElems);
                _pElems = pElemsNew;

                for (long i = 0; i < _bufferSizeBytes; i += _chunkSize)
                {
                    _freeStack.Push(_bufferSizeBytes + i);
                }

                _bufferSizeBytes <<= 1;
            }

            childOffset = (byte*)_freeStack.Pop();
        }
        else
        {
            childOffset = chunkOffset + bucketHeader.ChildOffset;

            Debug.Assert(bucketHeader.ChildOffset > 0);
        }

        return TryAdd(chunkOffset + bucketHeader.ChildOffset, entryIndex * (keyRangeLength / _bucketsPerChunk), keyRangeLength / _bucketsPerChunk, key, value);
    }

    public TValue this[long key]
    {

    }

    public bool TryGetValue(long key, out TValue value)
    {

    }

    public bool TryRemove(long key, out TValue value)
    {
        // also clear any headers
    }

    public IEnumerable<(long Key, TValue Value)> GetNearest(long key)
    {

    }

    public IEnumerable<(long Key, TValue Value)> GetRange(long keyRangeStart, long keyRangeEnd)
    {

    }

    public void Clear()
    {
        _freeStack.Clear();

        //todo: clear memory
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            NativeMemory.Free(_pElems);
        }

        _disposed = true;
    }

    ~NativeLocalDictionary()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
*/