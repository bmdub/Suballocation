using Suballocation.Collections;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

// buddy system
// use heap for free list, 1 for each size and side.  pop and push as needed to get nearest for given size.
// still could be quite far away... maybe do that operation for all sizes higher that are not empty, then decide.
// need custom heap

public unsafe class LocalBuddySuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private static readonly Comparer<BlockHeader> _headerStartComparer;
    private static readonly Comparer<BlockHeader> _headerEndComparer;

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
    private bool _disposed;

    static LocalBuddySuballocator()
    {
        _headerStartComparer = Comparer<BlockHeader>.Create((a, b) => a.Index.CompareTo(b.Index));
        _headerEndComparer = Comparer<BlockHeader>.Create((a, b) => (a.Index + a.BlockLength).CompareTo(b.Index + b.BlockLength));
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
            _freeBlocksPrev[i] = new NativeHeap<BlockHeader>(comparer: _headerEndComparer);
            _freeBlocksNext[i] = new NativeHeap<BlockHeader>(comparer: _headerStartComparer);
        }

        InitBlocks();
    }

    private void InitBlocks()
    {
        _freeBlockFlagsPrev = 0;
        _freeBlockFlagsNext = 0;

        long index = 0;

        for (long blockLength = _maxBlockLength; blockLength > 0; blockLength >>= 1)
        {
            if (blockLength > _indexLength - index)
            {
                continue;
            }

            var blockLengthLog = BitOperations.Log2((ulong)blockLength);

            _freeBlocksNext[blockLengthLog].Enqueue(new BlockHeader() { Index = index, BlockLengthLog = blockLengthLog });

            _freeBlockFlagsNext |= blockLength;

            index += blockLength;
        }
    }

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index * MinBlockLength, rawSegment.Length);
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

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index * MinBlockLength, rawSegment.Length);
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

        long blockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)length) >> BitOperations.Log2((ulong)MinBlockLength);

        int minFreeBlockIndex = BitOperations.Log2((ulong)blockLength);

        long mask = ~(Math.Max(1, (minFreeBlockIndex << 1)) - 1);

        long matchingBlockLengthsPrev = mask & _freeBlockFlagsPrev;
        long matchingBlockLengthsNext = mask & _freeBlockFlagsNext;

        // Choose the side with the largest free block, as a heuristic
        long matchingBlockLengths = matchingBlockLengthsNext;
        ref long freeBlockFlags = ref _freeBlockFlagsNext;
        var freeBlocks = _freeBlocksNext;

        if (BitOperations.PopCount((ulong)matchingBlockLengthsPrev) > BitOperations.PopCount((ulong)matchingBlockLengthsNext))
        {
            matchingBlockLengths = matchingBlockLengthsPrev;
            freeBlockFlags = ref _freeBlockFlagsPrev;
            freeBlocks = _freeBlocksPrev;
        }

        if (matchingBlockLengths == 0)
        {
            throw new OutOfMemoryException();
        }

        int freeBlockIndex = BitOperations.Log2((ulong)matchingBlockLengths);

        var header = freeBlocks[freeBlockIndex].Dequeue();

        .//todo: combine buddies here? or where? can/should defer?
        //heap: if one split, could dequeue and enqueue optimize at once.
        //consider super blocks

        if (_freeBlocksNext[freeBlockIndex].Length + _freeBlocksPrev[freeBlockIndex].Length == 0)
        {
            freeBlockFlags &= ~header.BlockLength;
        }

        for (int i = minFreeBlockIndex; i < freeBlockIndex; i++)
        {
            // Split in half
            header = header with { BlockLengthLog = header.BlockLengthLog - 1 };

            var header2 = new BlockHeader() { Index = header.Index + (1L << (header.BlockLengthLog - 1)), BlockLengthLog = header.BlockLengthLog };

            freeBlocks[header.BlockLengthLog].Enqueue(header2);
            freeBlockFlags |= header.BlockLength;
        }

        Allocations++;
        BlocksUsed += header.BlockLength;

        return (header.Index, header.BlockLength);
    }

    private unsafe void Free(long offset, long length)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalBuddySuballocator<T>));

        long index = offset / MinBlockLength;

        if (index < 0 || index >= _indexLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int lengthLog = BitOperations.Log2((ulong)length);

        var header = new BlockHeader() { Index = index, BlockLengthLog = lengthLog };

        if (_freeBlocksNext[lengthLog].Length > 0 && index >= _freeBlocksNext[lengthLog].Peek().Index)
        {
            _freeBlocksNext[lengthLog].Enqueue(header);
            _freeBlockFlagsNext |= header.BlockLength;
        }
        else if (_freeBlocksPrev[lengthLog].Length > 0 && index <= _freeBlocksPrev[lengthLog].Peek().Index)
        {
            _freeBlocksPrev[lengthLog].Enqueue(header);
            _freeBlockFlagsPrev |= header.BlockLength;
        }
        else
        {
            _freeBlocksNext[lengthLog].Enqueue(header);
            _freeBlockFlagsNext |= header.BlockLength;
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
        private readonly byte _lengthLog;
        private readonly long _index;

        public int BlockLengthLog { get => _lengthLog; init => _lengthLog = (byte)value; }
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

        public long Index { get => _index; init => _index = value; }
    }
}
