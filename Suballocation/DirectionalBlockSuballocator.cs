using System.Buffers;

namespace Suballocation;

/// <summary>
/// A suballocator that uses a heuristic to converge on a nearby free segment in either direction when locating a segment to rent.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
public unsafe sealed class DirectionalBlockSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
    private readonly IndexEntry* _pIndex;
    private readonly long _blockLength;
    private readonly long _blockCount;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private long _freeBlockBalance;
    private long _currentIndex;
    private bool directionForward = true;
    private bool _disposed;

    public DirectionalBlockSuballocator(long length, long blockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        CapacityLength = length;

        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)), BlockCountPrev = i == 0 ? 0 : int.MaxValue };
            _freeBlockBalance += _pIndex[i].BlockCount;
        }
    }

    public DirectionalBlockSuballocator(T* pElems, long length, long blockLength = 1)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        CapacityLength = length;

        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
        _pElems = pElems;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)), BlockCountPrev = i == 0 ? 0 : int.MaxValue };
            _freeBlockBalance += _pIndex[i].BlockCount;
        }
    }

    public DirectionalBlockSuballocator(Memory<T> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        CapacityLength = data.Length;

        _blockCount = data.Length / blockLength;
        if (data.Length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)), BlockCountPrev = i == 0 ? 0 : int.MaxValue };
            _freeBlockBalance += _pIndex[i].BlockCount;
        }
    }

    public long UsedBytes => UsedLength * Unsafe.SizeOf<T>();

    public long CapacityBytes => CapacityLength * Unsafe.SizeOf<T>();

    public long Allocations { get; private set; }

    public long UsedLength { get; private set; }

    public long CapacityLength { get; init; }

    public T* PElems => _pElems;

    public byte* PBytes => (byte*)_pElems;

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));
        if (length <= 0 || length > int.MaxValue * _blockLength)
            throw new ArgumentOutOfRangeException(nameof(length), $"{nameof(length)} must be greater than 0 and less than Int32.Max times the block length.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Offset, rawSegment.Length);
    }

    public void ReturnResource(NativeMemorySegmentResource<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    public NativeMemorySegment<T> Rent(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));
        if (length <= 0 || length > int.MaxValue * _blockLength)
            throw new ArgumentOutOfRangeException(nameof(length), $"{nameof(length)} must be greater than 0 and less than Int32.Max times the block length.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Offset, rawSegment.Length);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Offset, long Length) Alloc(long length)
    {
        int blockCount = (int)(length / _blockLength);
        if (length % _blockLength > 0)
        {
            blockCount++;
        }

        long initialIndex = _currentIndex;
        long initialBalance = _freeBlockBalance;

        // Decide which direction to go, based on current index distance from either end, and the amount of free space left on either side.
        bool directionForwardPrev = directionForward;
        directionForward = false;
        var distanceFromCenter = ((_currentIndex + 1) / (double)_blockCount) - .5;
        var balance = _freeBlockBalance / (double)_blockCount;
        var dir = directionForwardPrev ? 1 : -1;
        if (balance * 1 + distanceFromCenter * .5 + dir * 1 >= 0)
        {
            directionForward = true;
        }

        int turnaroundCount = 0;

        void AdvanceIndex()
        {
            ref IndexEntry fromHeader = ref _pIndex[_currentIndex];

            if (directionForward)
            {
                if (fromHeader.Occupied == false)
                {
                    _freeBlockBalance -= fromHeader.BlockCount << 1;
                }

                _currentIndex = _currentIndex + fromHeader.BlockCount;

                if (_currentIndex >= _blockCount)
                {
                    _currentIndex = initialIndex;
                    _freeBlockBalance = initialBalance;

                    directionForward = false;

                    if (++turnaroundCount == 2)
                    {
                        throw new OutOfMemoryException();
                    }
                }
            }
            else
            {
                _currentIndex = _currentIndex - fromHeader.BlockCountPrev;

                if (_currentIndex < 0 || fromHeader.BlockCountPrev == 0)
                {
                    _currentIndex = initialIndex;
                    _freeBlockBalance = initialBalance;

                    directionForward = true;

                    if (++turnaroundCount == 2)
                    {
                        throw new OutOfMemoryException();
                    }
                }
                else if (_pIndex[_currentIndex].Occupied == false)
                {
                    _freeBlockBalance += fromHeader.BlockCountPrev << 1;
                }
            }

            //Console.WriteLine(directionForward);
        }

        for (; ; )
        {
            ref IndexEntry header = ref _pIndex[_currentIndex];

            if (header.Occupied)
            {
                AdvanceIndex();
                continue;
            }

            if (header.BlockCount < blockCount)
            {
                AdvanceIndex();
                continue;
            }

            long targetIndex = _currentIndex;

            if (header.BlockCount > blockCount)
            {
                if (directionForward)
                {
                    if (targetIndex + header.BlockCount < _blockCount)
                    {
                        ref IndexEntry nextEntry = ref _pIndex[targetIndex + header.BlockCount];
                        nextEntry = nextEntry with { BlockCountPrev = header.BlockCount - blockCount };
                    }

                    var leftoverEntry = new IndexEntry() { BlockCount = header.BlockCount - blockCount, BlockCountPrev = blockCount };
                    _pIndex[targetIndex + blockCount] = leftoverEntry;

                    header = header with { Occupied = true, BlockCount = blockCount };

                    _freeBlockBalance -= blockCount;
                    AdvanceIndex();
                }
                else
                {
                    if (targetIndex + header.BlockCount < _blockCount)
                    {
                        ref IndexEntry nextEntry = ref _pIndex[targetIndex + header.BlockCount];
                        nextEntry = nextEntry with { BlockCountPrev = blockCount };
                    }

                    var leftoverEntry = new IndexEntry() { BlockCount = header.BlockCount - blockCount, BlockCountPrev = header.BlockCountPrev };
                    _pIndex[targetIndex] = leftoverEntry;

                    targetIndex += leftoverEntry.BlockCount;
                    _pIndex[targetIndex] = new IndexEntry { Occupied = true, BlockCount = blockCount, BlockCountPrev = leftoverEntry.BlockCount };

                    _freeBlockBalance -= blockCount;
                }
            }
            else
            {
                header = header with { Occupied = true };
                AdvanceIndex();
            }

            Allocations++;
            UsedLength += length;

            return new(targetIndex * _blockLength, blockCount * _blockLength);
        }
    }

    private unsafe void Free(long index, long length)
    {
        long blockIndex = index / _blockLength;
        int blockCount = (int)(length / _blockLength);

        ref IndexEntry header = ref _pIndex[blockIndex];

        if (header.BlockCount != blockCount)
        {
            throw new ArgumentException($"No rented segment found at index {index} with length {length}.");
        }

        header = header with { Occupied = false };

        Allocations--;
        UsedLength -= length;

        // While we're here, see if we can combine the nearby segments with this one.
        long rangeStart = blockIndex;
        int rangeLength = blockCount;

        var nextIndex = blockIndex + header.BlockCount;

        while (nextIndex < _blockCount)
        {
            ref IndexEntry nextHeader = ref _pIndex[nextIndex];

            if (nextHeader.Occupied || header.BlockCount + nextHeader.BlockCount > int.MaxValue) 
            {
                break;
            }

            header = header with { BlockCount = header.BlockCount + nextHeader.BlockCount };

            rangeLength = header.BlockCount;

            nextIndex += nextHeader.BlockCount;
        }

        var prevPrevIndex = blockIndex;
        var prevIndex = blockIndex - header.BlockCountPrev;

        while (prevIndex >= 0 && prevIndex != prevPrevIndex)
        {
            ref IndexEntry prevHeader = ref _pIndex[prevIndex];

            if (prevHeader.Occupied)
            {
                break;
            }

            ref IndexEntry nextHeader = ref _pIndex[prevIndex + prevHeader.BlockCount];

            if (prevHeader.BlockCount + nextHeader.BlockCount > int.MaxValue)
            {
                break;
            }

            prevHeader = prevHeader with { BlockCount = prevHeader.BlockCount + nextHeader.BlockCount };

            rangeStart = prevIndex;
            rangeLength = prevHeader.BlockCount;

            prevPrevIndex = prevIndex;
            prevIndex -= prevHeader.BlockCountPrev;
        }

        // If we combined free segments, then update the previous size of the next occupied segment
        if(rangeLength != blockCount && rangeStart + rangeLength < _blockCount)
        {
            ref IndexEntry nextHeader = ref _pIndex[rangeStart + rangeLength];
            nextHeader = nextHeader with { BlockCountPrev = rangeLength };
        }

        if (_currentIndex > rangeStart && _currentIndex < rangeStart + rangeLength)
        {
            _freeBlockBalance += (_currentIndex - rangeStart) << 1;
            _currentIndex = rangeStart;
        }
        else
        {
            if (rangeStart >= _currentIndex)
            {
                _freeBlockBalance += blockCount;
            }
            else
            {
                _freeBlockBalance -= blockCount;
            }
        }

        //Debug.Assert(rangeStart + rangeLength <= _blockCount || _pIndex[rangeStart].BlockCount > 0);
        //Debug.Assert(rangeStart + rangeLength >= _blockCount || _pIndex[rangeStart + rangeLength].BlockCount > 0);
    }

    public void Clear()
    {
        Allocations = 0;
        UsedLength = 0;
        _currentIndex = 0;
        _freeBlockBalance = 0;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
            _freeBlockBalance += _pIndex[i].BlockCount;
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
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

    ~DirectionalBlockSuballocator()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    readonly struct IndexEntry
    {
        private readonly uint _general1;
        private readonly int _general2;

        public bool Occupied { get => (_general1 & 0x10000000u) != 0; init => _general1 = value ? (_general1 | 0x10000000u) : (_general1 & 0xEFFFFFFFu); }
        public int BlockCount { get => (int)(_general1 & 0xEFFFFFFFu); init => _general1 = (_general1 & 0x10000000u) | ((uint)value & 0xEFFFFFFFu); }
        public int BlockCountPrev { get => _general2; init => _general2 = value; }
    }
}
