using Suballocation.Collections;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

public unsafe class LocalBuddySuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private static readonly Comparer<BlockHeader> _nextElementComparer;
    private static readonly Comparer<BlockHeader> _prevElementComparer;

    public long MinBlockLength;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private T* _pElems;
    private NativeHeap<BlockHeader>[] _freeBlocksPrev = null!;
    private NativeHeap<BlockHeader>[] _freeBlocksNext = null!;
    private long _maxBlockLength;
    private long _indexLength;
    private long _freeBlockFlagsPrev;
    private long _freeBlockFlagsNext;
    private long _lastWriteIndex;
    private long _lastLastWriteIndex;
    private bool _disposed;

    static LocalBuddySuballocator()
    {
        _nextElementComparer = Comparer<BlockHeader>.Create((a, b) => a.Index.CompareTo(b.Index));
        _prevElementComparer = Comparer<BlockHeader>.Create((a, b) => (b.Index + b.BlockLength).CompareTo(a.Index + a.BlockLength));
    }

    public LocalBuddySuballocator(long length, long minBlockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");

        LengthTotal = length;
        _privatelyOwned = true;

        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());

        Init(minBlockLength);
    }

    public LocalBuddySuballocator(T* pData, long length, long minBlockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");
        if (pData == null) throw new ArgumentNullException(nameof(pData));

        LengthTotal = length;

        _pElems = pData;

        Init(minBlockLength);
    }

    public LocalBuddySuballocator(Memory<T> data, long minBlockLength = 1)
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
        _maxBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)_indexLength);

        var blockLengthLog = BitOperations.Log2((ulong)_indexLength) + 1;
        _freeBlocksPrev = new NativeHeap<BlockHeader>[blockLengthLog];
        _freeBlocksNext = new NativeHeap<BlockHeader>[blockLengthLog];

        for (int i = 0; i < blockLengthLog; i++)
        {
            _freeBlocksPrev[i] = new NativeHeap<BlockHeader>(comparer: _prevElementComparer);
            _freeBlocksNext[i] = new NativeHeap<BlockHeader>(comparer: _nextElementComparer);
        }

        InitBlocks();
    }

    private void InitBlocks()
    {
        _freeBlockFlagsPrev = 0;
        _freeBlockFlagsNext = 0;

        _freeBlocksNext[^1].Enqueue(new BlockHeader() { Index = 0, BlockLength = _indexLength });
        _freeBlockFlagsNext |= _indexLength;
    }

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index * MinBlockLength, rawSegment.Length * MinBlockLength);
    }

    public void ReturnResource(NativeMemorySegmentResource<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    public NativeMemorySegment<T> Rent(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index * MinBlockLength, rawSegment.Length * MinBlockLength);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Index, long Length) Alloc(long length)
    {
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));

        long blockLength = Math.Max(1, (long)BitOperations.RoundUpToPowerOf2((ulong)length) >> BitOperations.Log2((ulong)MinBlockLength));

        int minFreeBlockIndex = BitOperations.Log2((ulong)blockLength);

        long maskEqualLarger = ~((1 << minFreeBlockIndex) - 1);
        long maskSmaller = ~maskEqualLarger;

        long matchingBlockLengthsPrev = maskEqualLarger & _freeBlockFlagsPrev;
        long matchingBlockLengthsNext = maskEqualLarger & _freeBlockFlagsNext;
        long matchingBlockLengthsBoth = matchingBlockLengthsPrev | matchingBlockLengthsNext;

        // If no space left, check the smaller free blocks that are nearby, see if they can be combined.
        long smallerBlockLengths = (_freeBlockFlagsPrev | _freeBlockFlagsNext) & maskSmaller;

        /*while (matchingBlockLengthsBoth == 0)
        {
            int smallerBlockIndex = BitOperations.TrailingZeroCount((ulong)smallerBlockLengths);

            if (smallerBlockIndex == 64)
            {
                break;
            }

            while (_freeBlocksNext[smallerBlockIndex].Length > 1)
            {
                var header1 = _freeBlocksNext[smallerBlockIndex].Dequeue();
                var header2 = _freeBlocksNext[smallerBlockIndex].Peek();

                if (header2.Index != header1.Index + header1.BlockLength)
                {
                    _freeBlocksNext[smallerBlockIndex].Enqueue(header1);
                    break;
                }

                _freeBlocksNext[smallerBlockIndex].Dequeue();

                header1 = header1 with { BlockLength = header1.BlockLength + header2.BlockLength };

                _freeBlocksNext[smallerBlockIndex].Enqueue(header1);
                _freeBlockFlagsNext |= (1L << smallerBlockIndex);
            }

            while (_freeBlocksPrev[smallerBlockIndex].Length > 1)
            {
                var header1 = _freeBlocksPrev[smallerBlockIndex].Dequeue();
                var header2 = _freeBlocksPrev[smallerBlockIndex].Peek();

                if (header2.Index + header2.BlockLength != header1.Index)
                {
                    _freeBlocksPrev[smallerBlockIndex].Enqueue(header1);
                    break;
                }

                _freeBlocksPrev[smallerBlockIndex].Dequeue();

                header1 = header2 with { BlockLength = header1.BlockLength + header2.BlockLength };

                _freeBlocksPrev[smallerBlockIndex].Enqueue(header1);
                _freeBlockFlagsPrev |= (1L << smallerBlockIndex);
            }

            smallerBlockLengths &= ~(1L << smallerBlockIndex);

            matchingBlockLengthsPrev = maskEqualLarger & _freeBlockFlagsPrev;
            matchingBlockLengthsNext = maskEqualLarger & _freeBlockFlagsNext;
            matchingBlockLengthsBoth = matchingBlockLengthsPrev | matchingBlockLengthsNext;
        }

        // If still no space left, look through ALL free blocks and try to combine them.
        long allBlockLengths = _freeBlockFlagsPrev | _freeBlockFlagsNext;

        while (matchingBlockLengthsBoth == 0)
        {
            int blockIndex = BitOperations.TrailingZeroCount((ulong)allBlockLengths);

            if (blockIndex == 64)
            {
                break;
            }

            if (_freeBlocksNext[blockIndex].Length > 0)
            {
                var header1 = _freeBlocksNext[blockIndex].Dequeue();

                while (_freeBlocksNext[blockIndex].Length > 0)
                {
                    var header2 = _freeBlocksNext[blockIndex].Dequeue();

                    if (header2.Index != header1.Index + header1.BlockLength)
                    {
                        _freeBlocksPrev[blockIndex].Enqueue(header1);
                        _freeBlockFlagsPrev |= (1L << blockIndex);
                        header1 = header2;
                        break;
                    }

                    header1 = header1 with { BlockLength = header1.BlockLength + header2.BlockLength };
                }

                _freeBlocksPrev[blockIndex].Enqueue(header1);
                _freeBlockFlagsPrev |= (1L << blockIndex);

                if (_freeBlocksNext[blockIndex].Length == 0)
                {
                    _freeBlockFlagsNext &= ~(1L << blockIndex);
                }
            }

            if (_freeBlocksPrev[blockIndex].Length > 0)
            {
                var header1 = _freeBlocksPrev[blockIndex].Dequeue();

                while (_freeBlocksPrev[blockIndex].Length > 0)
                {
                    var header2 = _freeBlocksPrev[blockIndex].Dequeue();

                    if (header2.Index + header2.BlockLength != header1.Index)
                    {
                        _freeBlocksNext[blockIndex].Enqueue(header1);
                        _freeBlockFlagsNext |= (1L << blockIndex);
                        header1 = header2;
                        break;
                    }

                    header1 = header2 with { BlockLength = header1.BlockLength + header2.BlockLength };
                }

                _freeBlocksNext[blockIndex].Enqueue(header1);
                _freeBlockFlagsNext |= (1L << blockIndex);

                if (_freeBlocksPrev[blockIndex].Length == 0)
                {
                    _freeBlockFlagsPrev &= ~(1L << blockIndex);
                }
            }

            allBlockLengths &= ~(1L << blockIndex);

            matchingBlockLengthsPrev = maskEqualLarger & _freeBlockFlagsPrev;
            matchingBlockLengthsNext = maskEqualLarger & _freeBlockFlagsNext;
            matchingBlockLengthsBoth = matchingBlockLengthsPrev | matchingBlockLengthsNext;
        }

        if (matchingBlockLengthsBoth == 0)
        {
            throw new OutOfMemoryException();
        }*/

        //
        /*
        int freeBlockIndexNext = BitOperations.TrailingZeroCount((ulong)matchingBlockLengthsNext);
        long distanceNext = freeBlockIndexNext >= 64 ? long.MaxValue : Math.Abs(_freeBlocksNext[freeBlockIndexNext].Peek().Index - _lastWriteIndex);

        int freeBlockIndexPrev = BitOperations.TrailingZeroCount((ulong)matchingBlockLengthsPrev);
        long distancePrev = freeBlockIndexPrev >= 64 ? long.MaxValue : Math.Abs(_freeBlocksPrev[freeBlockIndexPrev].Peek().Index - _lastWriteIndex);

        long matchingBlockLengths = matchingBlockLengthsNext;
        ref long freeBlockFlags = ref _freeBlockFlagsNext;
        var freeBlocks = _freeBlocksNext;
        var freeBlocksOther = _freeBlocksPrev;

        //if (BitOperations.PopCount((ulong)matchingBlockLengthsPrev) > BitOperations.PopCount((ulong)matchingBlockLengthsNext))
        //if(distancePrev < distanceNext)
        //if (freeBlockIndexPrev < freeBlockIndexNext)
        //if(distancePrev != long.MaxValue && (_lastWriteIndex < _lastLastWriteIndex || distanceNext == long.MaxValue))
        if(matchingBlockLengthsPrev != 0 && (_lastWriteIndex < _lastLastWriteIndex || distanceNext == long.MaxValue))
        {
            matchingBlockLengths = matchingBlockLengthsPrev;
            freeBlockFlags = ref _freeBlockFlagsPrev;
            freeBlocks = _freeBlocksPrev;
            freeBlocksOther = _freeBlocksNext;
        }*/

        ref long freeBlockFlags = ref _freeBlockFlagsNext;
        var freeBlocks = _freeBlocksNext;
        var freeBlocksOther = _freeBlocksPrev;
        int freeBlockIndex = -1;

        long minDistance = long.MaxValue;

        long allBlockLengths = _freeBlockFlagsPrev | _freeBlockFlagsNext;

        for (; ; )
        {
            int blockIndex = BitOperations.TrailingZeroCount((ulong)allBlockLengths);

            if (blockIndex == 64)
            {
                break;
            }

            while (_freeBlocksNext[blockIndex].TryPeek(out var moveHeader) && moveHeader.Index < _lastWriteIndex)
            {
                _freeBlocksPrev[blockIndex].Enqueue(_freeBlocksNext[blockIndex].Dequeue());
                _freeBlockFlagsPrev |= (1L << blockIndex);

                if (_freeBlocksNext[blockIndex].Length == 0)
                {
                    _freeBlockFlagsNext &= ~(1L << blockIndex);
                }
            }

            while (_freeBlocksPrev[blockIndex].TryPeek(out var moveHeader2) && moveHeader2.Index > _lastWriteIndex)
            {
                _freeBlocksNext[blockIndex].Enqueue(_freeBlocksPrev[blockIndex].Dequeue());
                _freeBlockFlagsNext |= (1L << blockIndex);

                if (_freeBlocksPrev[blockIndex].Length == 0)
                {
                    _freeBlockFlagsPrev &= ~(1L << blockIndex);
                }
            }

            if (_freeBlocksNext[blockIndex].TryDequeue(out var candHeader))
            {
                while (_freeBlocksNext[blockIndex].TryPeek(out var otherHeader) && otherHeader.Index == candHeader.Index + candHeader.BlockLength)
                {
                    _freeBlocksNext[blockIndex].Dequeue();

                    candHeader = candHeader with { BlockLength = candHeader.BlockLength + otherHeader.BlockLength };
                }

                var newBlockIndex = BitOperations.Log2((ulong)candHeader.BlockLength);

                _freeBlocksNext[newBlockIndex].Enqueue(candHeader);

                if (newBlockIndex != blockIndex)
                {
                    _freeBlockFlagsNext |= (1L << newBlockIndex);

                    if (_freeBlocksNext[blockIndex].Length == 0)
                    {
                        _freeBlockFlagsNext &= ~(1L << blockIndex);
                    }
                }
                else
                {
                    var distance = Math.Abs(candHeader.Index + candHeader.BlockLength - _lastWriteIndex);

                    if (distance < minDistance && candHeader.BlockLength >= length)
                    {
                        minDistance = distance;

                        freeBlockFlags = ref _freeBlockFlagsNext;
                        freeBlocks = _freeBlocksNext;
                        freeBlocksOther = _freeBlocksPrev;
                        freeBlockIndex = newBlockIndex;
                    }
                }
            }

            if (_freeBlocksPrev[blockIndex].TryDequeue(out var candHeader2))
            {
                while (_freeBlocksPrev[blockIndex].TryPeek(out var otherHeader) && otherHeader.Index + otherHeader.BlockLength == candHeader2.Index)
                {
                    _freeBlocksPrev[blockIndex].Dequeue();

                    candHeader2 = candHeader2 with { Index = otherHeader.Index, BlockLength = candHeader2.BlockLength + otherHeader.BlockLength };
                }

                var newBlockIndex = BitOperations.Log2((ulong)candHeader2.BlockLength);

                _freeBlocksPrev[newBlockIndex].Enqueue(candHeader2);

                if (newBlockIndex != blockIndex)
                {
                    _freeBlockFlagsPrev |= (1L << newBlockIndex);

                    if (_freeBlocksPrev[blockIndex].Length == 0)
                    {
                        _freeBlockFlagsPrev &= ~(1L << blockIndex);
                    }
                }
                else
                {
                    var distance = Math.Abs(candHeader2.Index - _lastWriteIndex);

                    if (distance < minDistance && candHeader2.BlockLength >= length)
                    {
                        minDistance = distance;

                        freeBlockFlags = ref _freeBlockFlagsPrev;
                        freeBlocks = _freeBlocksPrev;
                        freeBlocksOther = _freeBlocksNext;
                        freeBlockIndex = newBlockIndex;
                    }
                }
            }

            allBlockLengths &= ~(1L << blockIndex);
        }

        var header = freeBlocks[freeBlockIndex].Dequeue();

        // Combine any free buddies we see while we're here.
        /*if (freeBlocks == _freeBlocksNext)
        {
            while (freeBlocks[freeBlockIndex].Length > 0)
            {
                var header2 = freeBlocks[freeBlockIndex].Peek();

                if (header2.Index != header.Index + header.BlockLength)
                {
                    break;
                }

                freeBlocks[freeBlockIndex].Dequeue();

                header = header with { BlockLength = blockLength + header2.BlockLength };
            }
        }
        else
        {
            while (freeBlocks[freeBlockIndex].Length > 0)
            {
                var header2 = freeBlocks[freeBlockIndex].Peek();

                if (header2.Index + header2.BlockLength != header.Index)
                {
                    break;
                }

                freeBlocks[freeBlockIndex].Dequeue();

                header = header with { Index = header2.Index, BlockLength = blockLength + header2.BlockLength };
            }
        }*/

        if (freeBlocks[freeBlockIndex].Length == 0)
        {
            freeBlockFlags &= ~(1L << freeBlockIndex);
        }

        if (header.BlockLength > blockLength)
        {
            // Split out blocks
            var header2 = new BlockHeader() { Index = header.Index + blockLength, BlockLength = header.BlockLength - blockLength };
            var freeBlockLengthLog = BitOperations.Log2((ulong)header2.BlockLength);
            freeBlocks[freeBlockLengthLog].Enqueue(header2);
            freeBlockFlags |= (1L << freeBlockLengthLog);

            header = header with { BlockLength = blockLength };
        }

        Allocations++;
        BlocksUsed += header.BlockLength;

        _lastLastWriteIndex = _lastWriteIndex;
        _lastWriteIndex = header.Index + header.BlockLength;

        return (header.Index, header.BlockLength);
    }

    private unsafe void Free(long offset, long length)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));

        long index = offset / MinBlockLength;

        if (index < 0 || index >= _indexLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var freeBlockLengthLog = BitOperations.Log2((ulong)length);

        var header = new BlockHeader() { Index = index, BlockLength = length };

        if (_freeBlocksNext[freeBlockLengthLog].Length > 0 && index >= _freeBlocksNext[freeBlockLengthLog].Peek().Index)
        {
            _freeBlocksNext[freeBlockLengthLog].Enqueue(header);
            _freeBlockFlagsNext |= (1L << freeBlockLengthLog);
        }
        else if (_freeBlocksPrev[freeBlockLengthLog].Length > 0 && index <= _freeBlocksPrev[freeBlockLengthLog].Peek().Index)
        {
            _freeBlocksPrev[freeBlockLengthLog].Enqueue(header);
            _freeBlockFlagsPrev |= (1L << freeBlockLengthLog);
        }
        else
        {
            _freeBlocksNext[freeBlockLengthLog].Enqueue(header);
            _freeBlockFlagsNext |= (1L << freeBlockLengthLog);
        }

        Allocations--;
        BlocksUsed -= header.BlockLength;
    }

    public void Clear()
    {
        Allocations = 0;
        BlocksUsed = 0;

        foreach (var heap in _freeBlocksPrev.Concat(_freeBlocksNext))
        {
            heap.Clear();
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
                foreach (var heap in _freeBlocksPrev.Concat(_freeBlocksNext))
                {
                    heap.Dispose();
                }
            }

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
            }

            _disposed = true;
        }
    }

    ~LocalBuddySuballocator()
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
        private readonly long _index;
        private readonly long _lengthInBlocks;

        public long Index { get => _index; init => _index = value; }
        public long BlockLength { get => _lengthInBlocks; init => _lengthInBlocks = value; }
    }
}
