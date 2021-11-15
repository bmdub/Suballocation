using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

public unsafe class BuddySuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    public long MinBlockLength;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private T* _pElems;
    private BlockHeader* _pIndex;
    private long _maxBlockLength;
    private long _indexLength;
    private long _freeBlockIndexesFlags;
    private long[] _freeBlockIndexesStart = null!;
    private bool _disposed;

    public BuddySuballocator(long length, long minBlockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");

        LengthTotal = length;
        _privatelyOwned = true;

        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());

        Init(minBlockLength);
    }

    public BuddySuballocator(T* pData, long length, long minBlockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");
        if (pData == null) throw new ArgumentNullException(nameof(pData));

        LengthTotal = length;

        _pElems = pData;

        Init(minBlockLength);
    }

    public BuddySuballocator(Memory<T> data, long minBlockLength = 1)
    {
        if (data.Length == 0) throw new ArgumentOutOfRangeException(nameof(data), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > (uint)data.Length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(data.Length)}.");

        LengthTotal = (long)data.Length;
        _memoryHandle = data.Pin();

        _pElems = (T*)_memoryHandle.Pointer;

        Init(minBlockLength);
    }

    public long BlocksUsed { get; private set; }

    public long SizeUsed => LengthUsed * (long)Unsafe.SizeOf<T>();

    public long SizeTotal => LengthTotal * (long)Unsafe.SizeOf<T>();

    public long Allocations { get; private set; }

    public long LengthUsed { get => BlocksUsed * MinBlockLength; }

    public long LengthTotal { get; init; }

    public T* PElems => _pElems;

    private void Init(long minBlockLength)
    {
        MinBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)minBlockLength);

        _indexLength = LengthTotal >> BitOperations.Log2((ulong)MinBlockLength);
        _pIndex = (BlockHeader*)NativeMemory.AllocZeroed((nuint)(_indexLength * (long)sizeof(BlockHeader)));
        _maxBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)_indexLength);
        _freeBlockIndexesStart = new long[BitOperations.Log2((ulong)_maxBlockLength) + 1];

        InitBlocks();
    }

    private void InitBlocks()
    {
        _freeBlockIndexesStart.AsSpan().Fill(long.MaxValue);
        _freeBlockIndexesFlags = 0;

        long index = 0;

        for (long blockLength = _maxBlockLength; blockLength > 0; blockLength >>= 1)
        {
            if (blockLength > _indexLength - index)
            {
                continue;
            }

            var blockLengthLog = BitOperations.Log2((ulong)blockLength);

            ref BlockHeader header = ref _pIndex[index];

            header = header with { Occupied = false, BlockLengthLog = blockLengthLog, NextFree = long.MaxValue, PreviousFree = long.MaxValue };

            _freeBlockIndexesStart[blockLengthLog] = index;
            _freeBlockIndexesFlags |= blockLength;

            index += header.BlockLength;
        }
    }

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index * MinBlockLength, rawSegment.Length * MinBlockLength);
    }

    public void ReturnResource(NativeMemorySegmentResource<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    public NativeMemorySegment<T> Rent(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index * MinBlockLength, rawSegment.Length * MinBlockLength);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Index, long Length) Alloc(long length)
    {
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        long index = -1;
        long blockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)length) >> BitOperations.Log2((ulong)MinBlockLength);

        int minFreeBlockIndex = BitOperations.Log2((ulong)blockLength);

        long mask = ~(Math.Max(1, (minFreeBlockIndex << 1)) - 1);

        long matchingBlockLengths = mask & _freeBlockIndexesFlags;

        if (matchingBlockLengths == 0)
        {
            throw new OutOfMemoryException();
        }

        int freeBlockIndex = BitOperations.Log2((ulong)matchingBlockLengths);

        long freeBlockIndexIndex = _freeBlockIndexesStart[freeBlockIndex];

        if (freeBlockIndex != _pIndex[freeBlockIndexIndex].BlockLengthLog)
            Debugger.Break();

        for (int i = minFreeBlockIndex; i < freeBlockIndex; i++)
        {
            // Split in half

            ref BlockHeader header1 = ref _pIndex[freeBlockIndexIndex];

            //var temp = header1;

            RemoveFromFreeList(ref header1);

            header1 = header1 with { Occupied = false, BlockLengthLog = header1.BlockLengthLog - 1, NextFree = freeBlockIndexIndex + (1L << (header1.BlockLengthLog - 1)), PreviousFree = long.MaxValue };

            ref BlockHeader header2 = ref _pIndex[header1.NextFree];

            header2 = header2 with { Occupied = false, BlockLengthLog = header1.BlockLengthLog, NextFree = _freeBlockIndexesStart[header1.BlockLengthLog], PreviousFree = freeBlockIndexIndex };

            _freeBlockIndexesStart[header2.BlockLengthLog] = freeBlockIndexIndex;
            _freeBlockIndexesFlags |= header2.BlockLength;

            if (header1.BlockLengthLog != _pIndex[freeBlockIndexIndex].BlockLengthLog)
                Debugger.Break();
            if (header2.BlockLengthLog != _pIndex[header1.NextFree].BlockLengthLog)
                Debugger.Break();

            if (header2.NextFree != long.MaxValue)
            {
                ref BlockHeader nextHeader = ref _pIndex[header2.NextFree];
                nextHeader = nextHeader with { PreviousFree = header1.NextFree };
            }
        }

        ref BlockHeader header = ref _pIndex[freeBlockIndexIndex];

        RemoveFromFreeList(ref header);

        header = header with { Occupied = true, BlockLengthLog = minFreeBlockIndex, NextFree = long.MaxValue, PreviousFree = long.MaxValue };

        index = freeBlockIndexIndex;

        Allocations++;
        BlocksUsed += header.BlockLength;
        //if (LengthUsed > LengthTotal)
        //Debugger.Break();

        return (index, header.BlockLength);
    }

    private unsafe void Free(long offset, long length)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        long freeBlockIndexIndex = offset / MinBlockLength;

        if (freeBlockIndexIndex < 0 || freeBlockIndexIndex >= _indexLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        ref BlockHeader header = ref _pIndex[freeBlockIndexIndex];

        if (header.Occupied == false)
            throw new ArgumentException($"No rented segment at offset {offset} found.");

        //header = header with { Occupied = true, NextFree = long.MaxValue, PreviousFree = long.MaxValue };

        Allocations--;
        BlocksUsed -= header.BlockLength;

        void Combine(long blockIndexIndex, int lengthLog)
        {
            ref BlockHeader header = ref _pIndex[blockIndexIndex];

            var buddyBlockIndexIndex = blockIndexIndex ^ (1L << lengthLog);

            /*if (buddyBlockIndexIndex < _indexLength && _pIndex[buddyBlockIndexIndex].Occupied == false && _pIndex[buddyBlockIndexIndex].BlockLengthLog != lengthLog)
            {
                var temp = _pIndex[buddyBlockIndexIndex];
                if(_pIndex[buddyBlockIndexIndex].BlockLengthLog != 0)
                    Debugger.Break();
            }*/

            if (buddyBlockIndexIndex >= _indexLength || _pIndex[buddyBlockIndexIndex].Occupied || _pIndex[buddyBlockIndexIndex].BlockLengthLog != lengthLog)
            {
                // No buddy / the end of the buffer.
                var nextFree = _freeBlockIndexesStart[lengthLog] == blockIndexIndex ? long.MaxValue : _freeBlockIndexesStart[lengthLog];
                header = header with { Occupied = false, BlockLengthLog = lengthLog, NextFree = nextFree, PreviousFree = long.MaxValue };
                _freeBlockIndexesStart[lengthLog] = blockIndexIndex;
                _freeBlockIndexesFlags |= 1L << lengthLog;

                if (nextFree != long.MaxValue)
                {
                    ref BlockHeader nextHeader = ref _pIndex[header.NextFree];
                    nextHeader = nextHeader with { PreviousFree = blockIndexIndex };

                    if (lengthLog != nextHeader.BlockLengthLog)
                        Debugger.Break();
                }

                if (lengthLog != _pIndex[blockIndexIndex].BlockLengthLog)
                    Debugger.Break();

                return;
            }

            ref var buddyHeader = ref _pIndex[buddyBlockIndexIndex];

            if (buddyBlockIndexIndex < blockIndexIndex)
            {
                blockIndexIndex = buddyBlockIndexIndex;
            }
            else
            {
                buddyHeader = buddyHeader with { Occupied = true };
            }

            RemoveFromFreeList(ref buddyHeader);

            Combine(blockIndexIndex, lengthLog + 1);
        }

        Combine(freeBlockIndexIndex, header.BlockLengthLog);
    }

    private void RemoveFromFreeList(ref BlockHeader header)
    {
        if (header.NextFree != long.MaxValue)
        {
            ref BlockHeader nextHeader = ref _pIndex[header.NextFree];

            nextHeader = nextHeader with { PreviousFree = header.PreviousFree };

            if (header.PreviousFree != long.MaxValue)
            {
                ref BlockHeader prevHeader = ref _pIndex[header.PreviousFree];

                prevHeader = prevHeader with { NextFree = header.NextFree };

                nextHeader = nextHeader with { PreviousFree = header.PreviousFree };

                if (prevHeader.BlockLengthLog != header.BlockLengthLog && header.BlockLengthLog != 0)
                    Debugger.Break();

                if (prevHeader.BlockLengthLog != _pIndex[header.NextFree].BlockLengthLog && header.BlockLengthLog != 0)
                    Debugger.Break();
            }
            else
            {
                nextHeader = nextHeader with { PreviousFree = long.MaxValue };

                _freeBlockIndexesStart[header.BlockLengthLog] = header.NextFree;
                //_freeBlockIndexesFlags |= header.BlockLength;

                if (header.BlockLengthLog != _pIndex[header.NextFree].BlockLengthLog && header.BlockLengthLog != 0)
                    Debugger.Break();
            }
        }
        else if (header.PreviousFree != long.MaxValue)
        {
            ref BlockHeader prevHeader = ref _pIndex[header.PreviousFree];

            prevHeader = prevHeader with { NextFree = long.MaxValue };
        }
        else
        {
            _freeBlockIndexesStart[header.BlockLengthLog] = long.MaxValue;
            _freeBlockIndexesFlags &= ~header.BlockLength;
        }
    }

    public void Clear()
    {
        Allocations = 0;
        BlocksUsed = 0;

        for (long i = 0; i < _indexLength; i += uint.MaxValue / Unsafe.SizeOf<BlockHeader>())
        {
            uint length = (uint)Math.Min(uint.MaxValue / Unsafe.SizeOf<BlockHeader>(), _indexLength - i);

            Unsafe.InitBlock(_pIndex + i, 0, length * (uint)Unsafe.SizeOf<BlockHeader>());
        }

        InitBlocks();
    }

    public static long GetLengthRequiredToPreventDefragmentation(long maxLength, long maxBlockSize)
    {
        // Defoe, Delvin C., "Storage Coalescing" Report Number: WUCSE-2003-69 (2003). All Computer Science and Engineering Research.
        // https://openscholarship.wustl.edu/cse_research/1115 
        // M(log n + 2)/2
        return (maxLength * ((long)Math.Log(maxBlockSize) + 2)) >> 1;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            NativeMemory.Free(_pIndex);

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
            }

            _disposed = true;
        }
    }

    ~BuddySuballocator()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    readonly struct BlockHeader
    {
        private readonly byte _infoByte;
        private readonly long _prevFree;
        private readonly long _nextFree;

        public bool Occupied { get => (_infoByte & 0b1000_0000) == 0; init => _infoByte = value ? (byte)(_infoByte & 0b0111_1111) : (byte)(_infoByte | 0b1000_0000); }
        public int BlockLengthLog { get => (_infoByte & 0b0111_1111); init => _infoByte = (byte)((_infoByte & 0b1000_0000) | (value & 0b0111_1111)); }
        public long BlockLength
        {
            get => (1L << BlockLengthLog);
            init
            {
                if (BitOperations.IsPow2(value) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(BlockLength), "Must be a power of 2.");
                }

                BlockLengthLog = BitOperations.Log2((ulong)value);
            }
        }

        public long PreviousFree { get => _prevFree; init => _prevFree = value; }
        public long NextFree { get => _nextFree; init => _nextFree = value; }
    }
}
