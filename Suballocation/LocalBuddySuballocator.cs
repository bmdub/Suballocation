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
    private NativeHeap<BlockHeader> _freeBlocksPrev = null!;
    private NativeHeap<BlockHeader> _freeBlocksNext = null!;
    private long _maxBlockLength;
    private long _indexLength;
    private long _lastWriteIndex;
    private long _lastLastWriteIndex;
    private bool _disposed;

    static LocalBuddySuballocator()
    {
        _nextElementComparer = Comparer<BlockHeader>.Create((a, b) => a.Index.CompareTo(b.Index));
        _prevElementComparer = Comparer<BlockHeader>.Create((a, b) => b.Index.CompareTo(a.Index));
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
        _freeBlocksPrev = new NativeHeap<BlockHeader>(comparer: _prevElementComparer);
        _freeBlocksNext = new NativeHeap<BlockHeader>(comparer: _nextElementComparer);

        InitBlocks();
    }

    private void InitBlocks()
    {
        _freeBlocksNext.Enqueue(new BlockHeader() { Index = 0, BlockLength = _indexLength });
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

        while (_freeBlocksNext.TryPeek(out var moveHeaderNext) && moveHeaderNext.Index < _lastWriteIndex)
        {
            _freeBlocksPrev.Enqueue(_freeBlocksNext.Dequeue());
        }

        while (_freeBlocksPrev.TryPeek(out var moveHeaderPrev) && moveHeaderPrev.Index > _lastWriteIndex)
        {
            _freeBlocksNext.Enqueue(_freeBlocksPrev.Dequeue());
        }


        var distanceNext = long.MaxValue;
        var distancePrev = long.MaxValue;

        Queue<BlockHeader> siftQueueNext = new Queue<BlockHeader>();
        Queue<BlockHeader> siftQueuePrev = new Queue<BlockHeader>();

        while (distanceNext == long.MaxValue && distancePrev == long.MaxValue)
        {
            if (_freeBlocksNext.Length == 0 && _freeBlocksPrev.Length == 0)
            {
                break;
            }

            if (_freeBlocksNext.TryDequeue(out var nextHeader))
            {
                while (_freeBlocksNext.TryPeek(out var otherHeaderNext) && otherHeaderNext.Index == nextHeader.Index + nextHeader.BlockLength)
                {
                    _freeBlocksNext.Dequeue();

                    nextHeader = nextHeader with { BlockLength = nextHeader.BlockLength + otherHeaderNext.BlockLength };
                }

                if (nextHeader.BlockLength >= length)
                {
                    distanceNext = Math.Abs(nextHeader.Index - _lastWriteIndex);
                    _freeBlocksNext.Enqueue(nextHeader);
                }
                else
                {
                    siftQueueNext.Enqueue(nextHeader);
                }
            }

            if (_freeBlocksPrev.TryDequeue(out var prevHeader))
            {
                while (_freeBlocksPrev.TryPeek(out var otherHeaderPrev) && otherHeaderPrev.Index + otherHeaderPrev.BlockLength == prevHeader.Index)
                {
                    _freeBlocksPrev.Dequeue();

                    prevHeader = prevHeader with { Index = otherHeaderPrev.Index, BlockLength = prevHeader.BlockLength + otherHeaderPrev.BlockLength };
                }

                if (prevHeader.BlockLength >= length)
                {
                    distancePrev = Math.Abs(prevHeader.Index - _lastWriteIndex);
                    if (prevHeader.Index == 1040508)
                        Debugger.Break();
                    _freeBlocksPrev.Enqueue(prevHeader);
                    var temp = _freeBlocksPrev.Peek().Index + _freeBlocksPrev.Peek().BlockLength;
                    var temp2 = prevHeader.Index + prevHeader.BlockLength;
                    Debug.Assert(prevHeader.Index == _freeBlocksPrev.Peek().Index);
                }
                else
                {
                    siftQueuePrev.Enqueue(prevHeader);
                }
            }
        }

        if (distanceNext < distancePrev && _freeBlocksNext.TryPeek(out var h) && h.BlockLength < length)
        {
            Debugger.Break();
        }

        if (distancePrev < distanceNext && _freeBlocksPrev.TryPeek(out var h2) && h2.BlockLength < length)
        {
            Debugger.Break();
        }

        if (distanceNext == long.MaxValue && distancePrev == long.MaxValue)
        {
            foreach (var siftedHeader in siftQueueNext)
            {
                _freeBlocksNext.Enqueue(siftedHeader);
            }

            foreach (var siftedHeader in siftQueuePrev)
            {
                _freeBlocksPrev.Enqueue(siftedHeader);
            }

            throw new OutOfMemoryException();
        }

        BlockHeader header;

        if (distanceNext <= distancePrev)
        {
            header = _freeBlocksNext.Dequeue();

            if (header.BlockLength > blockLength)
            {
                // Split out blocks
                var splitHeader = new BlockHeader() { Index = header.Index + blockLength, BlockLength = header.BlockLength - blockLength };
                _freeBlocksNext.Enqueue(splitHeader);

                header = header with { BlockLength = blockLength };
            }

            foreach (var siftedHeader in siftQueueNext)
            {
                _freeBlocksPrev.Enqueue(siftedHeader);
            }

            foreach (var siftedHeader in siftQueuePrev)
            {
                _freeBlocksPrev.Enqueue(siftedHeader);
            }
        }
        else
        {
            header = _freeBlocksPrev.Dequeue();

            if (header.BlockLength > blockLength)
            {
                // Split out blocks
                var splitHeader = new BlockHeader() { Index = header.Index, BlockLength = header.BlockLength - blockLength };
                _freeBlocksPrev.Enqueue(splitHeader);

                header = header with { Index = header.Index + header.BlockLength - blockLength, BlockLength = blockLength };
            }

            foreach (var siftedHeader in siftQueuePrev)
            {
                _freeBlocksNext.Enqueue(siftedHeader);
            }

            foreach (var siftedHeader in siftQueueNext)
            {
                _freeBlocksNext.Enqueue(siftedHeader);
            }
        }

        if (header.BlockLength < length)
        {
            Debugger.Break();
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

        if (_freeBlocksNext.Length > 0 && index >= _freeBlocksNext.Peek().Index)
        {
            _freeBlocksNext.Enqueue(header);
        }
        else if (_freeBlocksPrev.Length > 0 && index <= _freeBlocksPrev.Peek().Index)
        {
            _freeBlocksPrev.Enqueue(header);
        }
        else
        {
            _freeBlocksNext.Enqueue(header);
        }

        Allocations--;
        BlocksUsed -= header.BlockLength;
    }

    public void Clear()
    {
        Allocations = 0;
        BlocksUsed = 0;
        _freeBlocksPrev.Clear();
        _freeBlocksNext.Clear();

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
                _freeBlocksPrev.Dispose();
                _freeBlocksNext.Dispose();
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
