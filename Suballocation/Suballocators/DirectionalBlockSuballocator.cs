using System.Buffers;
using System.Collections;

namespace Suballocation.Suballocators;

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
    private bool _directionForward = true;
    private bool _disposed;

    /// <summary>Creates a suballocator instance and allocates a buffer of the specified length.</summary>
    /// <param name="length">Element length of the buffer to allocate.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public DirectionalBlockSuballocator(long length, long blockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        CapacityLength = length;
        _blockLength = blockLength;
        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)_blockCount, (nuint)sizeof(IndexEntry));
        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;

        InitIndexes();
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="pElems">A pointer to a pinned memory buffer to use as the backing buffer for this suballocator.</param>
    /// <param name="length">Element length of the given memory buffer.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public DirectionalBlockSuballocator(T* pElems, long length, long blockLength = 1)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        CapacityLength = length;
        _blockLength = blockLength;
        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)_blockCount, (nuint)sizeof(IndexEntry));
        _pElems = pElems;

        InitIndexes();
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="data">A region of memory to use as the backing buffer for this suballocator.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public DirectionalBlockSuballocator(Memory<T> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        CapacityLength = data.Length;
        _blockLength = blockLength;
        _blockCount = data.Length / blockLength;
        if (data.Length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)_blockCount, (nuint)sizeof(IndexEntry));
        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;

        InitIndexes();
    }

    public long UsedBytes => UsedLength * Unsafe.SizeOf<T>();

    public long CapacityBytes => CapacityLength * Unsafe.SizeOf<T>();

    public long FreeBytes { get => CapacityBytes - UsedBytes; }

    public long Allocations { get; private set; }

    public long UsedLength { get; private set; }

    public long CapacityLength { get; init; }

    public long FreeLength { get => CapacityLength - UsedLength; }

    public T* PElems => _pElems;

    public byte* PBytes => (byte*)_pElems;

    /// <summary>Common construction logic.</summary>
    private void InitIndexes()
    {
        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)), BlockCountPrev = i == 0 ? 0 : int.MaxValue };
            _freeBlockBalance += _pIndex[i].BlockCount;
        }
    }

    public bool TryRent(long length, out NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));
        if (length <= 0 || length > int.MaxValue * _blockLength)
            throw new ArgumentOutOfRangeException(nameof(length), $"{nameof(length)} must be greater than 0 and less than Int32.Max times the block length.");

        // Convert to block space (divide length by block size).
        int blockCount = (int)(length / _blockLength);
        if (blockCount * _blockLength != length)
        {
            blockCount++;
        }

        long initialIndex = _currentIndex;
        long initialBalance = _freeBlockBalance;

        // Decide which direction to search, based on current index distance from either end, and the amount of free space left on either side.
        bool directionForwardPrev = _directionForward;
        _directionForward = false;
        var distanceFromCenter = ((_currentIndex + 1) / (double)_blockCount) - .5;
        var balance = _freeBlockBalance / (double)_blockCount;
        var dir = directionForwardPrev ? 1 : -1;
        if (balance * 1 + distanceFromCenter * .5 + dir * 1 >= 0) // TODO: Make configurable
        {
            _directionForward = true;
        }

        // If we turn around twice whilst searching, that means we've searched the entire collection.
        int turnaroundCount = 0;

        // Use this method to move to the next segment in the current direction.
        bool AdvanceIndex()
        {
            ref IndexEntry fromHeader = ref _pIndex[_currentIndex];

            if (_directionForward)
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

                    _directionForward = false;

                    if (++turnaroundCount == 2)
                    {
                        return false;
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

                    _directionForward = true;

                    if (++turnaroundCount == 2)
                    {
                        return false;
                    }
                }
                else if (_pIndex[_currentIndex].Occupied == false)
                {
                    _freeBlockBalance += fromHeader.BlockCountPrev << 1;
                }
            }

            return true;
        }

        // Find a large-enough free segment to return.
        for (; ; )
        {
            ref IndexEntry header = ref _pIndex[_currentIndex];

            if (header.Occupied)
            {
                if(AdvanceIndex() == false)
                {
                    segment = default;
                    return false;
                }

                continue;
            }

            if (header.BlockCount < blockCount)
            {
                if (AdvanceIndex() == false)
                {
                    segment = default;
                    return false;
                }

                continue;
            }

            // Big enough, and free to use...

            long targetIndex = _currentIndex;

            if (header.BlockCount > blockCount)
            {
                // Too large; split it into 1 occupied and 1 free segment.
                // We have to adjust segment sizes nearby to allow accurate traversal.

                if (_directionForward)
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

                    if (AdvanceIndex() == false)
                    {
                        segment = default;
                        return false;
                    }
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

                if (AdvanceIndex() == false)
                {
                    segment = default;
                    return false;
                }
            }

            Allocations++;
            UsedLength += blockCount * _blockLength;

            segment = new NativeMemorySegment<T>(_pElems + targetIndex * _blockLength, blockCount * _blockLength);
            return true;
        }
    }

    public bool TryReturn(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));

        long index = segment.PElems - _pElems;

        // Convert to block space (divide length by block size).
        long blockIndex = index / _blockLength;
        int blockCount = (int)(segment.Length / _blockLength);

        ref IndexEntry header = ref _pIndex[blockIndex];

        if (header.BlockCount != blockCount)
        {
            return false;
        }

        header = header with { Occupied = false };

        Allocations--;
        UsedLength -= blockCount * _blockLength;

        // While we're here, see if we can combine the nearby free segments with this one.
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

        return true;
    }

    public void Clear()
    {
        Allocations = 0;
        UsedLength = 0;
        _currentIndex = 0;
        _freeBlockBalance = 0;

        InitIndexes();
    }

    public IEnumerator<NativeMemorySegment<T>> GetEnumerator() =>
        GetOccupiedSegments().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<NativeMemorySegment<T>> GetOccupiedSegments()
    {
        long index = _currentIndex;

        IndexEntry GetEntry() => _pIndex[index];

        NativeMemorySegment<T> GenerateSegment(long blockCount) =>
            new NativeMemorySegment<T>(_pElems + index * _blockLength, blockCount * _blockLength);

        // Iterate backward from current head
        var entry = GetEntry();

        index -= entry.BlockCountPrev;

        while (index > 0 && entry.BlockCountPrev != 0)
        {
            entry = GetEntry();

            if (entry.Occupied == true)
            {
                yield return GenerateSegment(entry.BlockCount);
            }

            index -= entry.BlockCountPrev;
        }

        // Iterate forward
        index = _currentIndex;

        while (index < _blockCount)
        {
            entry = GetEntry();

            if (entry.Occupied == true)
            {
                yield return GenerateSegment(entry.BlockCount);
            }

            index += entry.BlockCount;
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
