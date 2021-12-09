using Suballocation.Collections;
using System.Buffers;
using System.Collections;

namespace Suballocation.Suballocators;

public unsafe sealed class DirectionalBlockSuballocator<TSeg> : DirectionalBlockSuballocator<TSeg, EmptyStruct>, ISuballocator<TSeg> where TSeg : unmanaged
{
    public DirectionalBlockSuballocator(long length, long blockLength = 1) : base(length, blockLength) { }

    public DirectionalBlockSuballocator(TSeg* pElems, long length, long blockLength = 1) : base(pElems, length, blockLength) { }

    public DirectionalBlockSuballocator(Memory<TSeg> data, long blockLength = 1) : base(data, blockLength) { }
}

/// <summary>
/// A suballocator that uses a heuristic to determine the direction in which to search for the next segment to rent.
/// </summary>
/// <typeparam name="TSeg">A blittable element type that defines the units to allocate.</typeparam>
/// <typeparam name="TTag">Type to be tied to each segment, as a separate entity from the segment contents.</typeparam>
public unsafe class DirectionalBlockSuballocator<TSeg, TTag> : ISuballocator<TSeg, TTag>, IDisposable where TSeg : unmanaged
{
    private readonly TSeg* _pElems;
    private readonly BigArray<IndexEntry> _index;
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

        Length = length;
        _blockLength = blockLength;
        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _index = new BigArray<IndexEntry>(_blockCount);
        _pElems = (TSeg*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<TSeg>());
        GC.AddMemoryPressure(length * Unsafe.SizeOf<TSeg>());
        _privatelyOwned = true;

        InitIndexes();

        SuballocatorTable<TSeg, TTag>.Register(this);
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="pElems">A pointer to a pinned memory buffer to use as the backing buffer for this suballocator.</param>
    /// <param name="length">Element length of the given memory buffer.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public DirectionalBlockSuballocator(TSeg* pElems, long length, long blockLength = 1)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        Length = length;
        _blockLength = blockLength;
        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _index = new BigArray<IndexEntry>(_blockCount);
        _pElems = pElems;

        InitIndexes();

        SuballocatorTable<TSeg, TTag>.Register(this);
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="data">A region of memory to use as the backing buffer for this suballocator.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public DirectionalBlockSuballocator(Memory<TSeg> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        Length = data.Length;
        _blockLength = blockLength;
        _blockCount = data.Length / blockLength;
        if (data.Length % blockLength > 0) _blockCount++;
        _index = new BigArray<IndexEntry>(_blockCount);
        _memoryHandle = data.Pin();
        _pElems = (TSeg*)_memoryHandle.Pointer;

        InitIndexes();

        SuballocatorTable<TSeg, TTag>.Register(this);
    }

    public long UsedBytes => Used * Unsafe.SizeOf<TSeg>();

    public long LengthBytes => Length * Unsafe.SizeOf<TSeg>();

    public long FreeBytes { get => LengthBytes - UsedBytes; }

    public long Allocations { get; private set; }

    public long Used { get; private set; }

    public long Length { get; init; }

    public long Free { get => Length - Used; }

    public TSeg* PElems => _pElems;

    public byte* PBytes => (byte*)_pElems;

    /// <summary>Common construction logic.</summary>
    private void InitIndexes()
    {
        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _index[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)), BlockCountPrev = i == 0 ? 0 : int.MaxValue };
            _freeBlockBalance += _index[i].BlockCount;
        }
    }

    public bool TryRent(long length, out NativeMemorySegment<TSeg, TTag> segment, TTag tag = default!)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<TSeg, TTag>));
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
        var distanceFromCenter = (((_currentIndex + 1) / (double)_blockCount) - .5) * 2;
        var balance = _freeBlockBalance / (double)_blockCount;
        var dir = directionForwardPrev ? 1 : -1;
       // Console.WriteLine($"{balance}, {distanceFromCenter}, {dir}");
        //distanceFromCenter = Math.Sign(distanceFromCenter) == Math.Sign(dir) ? distanceFromCenter : 0;
        if (balance * 1.0 + distanceFromCenter * 0.0 + dir * 0.3 >= 0) // TODO: Make configurable
        {
            _directionForward = true;
        }

        // If we turn around twice whilst searching, that means we've searched the entire collection.
        int turnaroundCount = 0;

        // Use this method to move to the next segment in the current direction.
        bool AdvanceIndex()
        {
            ref IndexEntry fromHeader = ref _index[_currentIndex];

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
                else if (_index[_currentIndex].Occupied == false)
                {
                    _freeBlockBalance += fromHeader.BlockCountPrev << 1;
                }
            }

            return true;
        }

        // Find a large-enough free segment to return.
        for (; ; )
        {
            ref IndexEntry header = ref _index[_currentIndex];

            if (header.Occupied)
            {
                if (AdvanceIndex() == false)
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

            // Big enough segment found, and free to use...

            long targetIndex = _currentIndex;

            if (header.BlockCount > blockCount)
            {
                // Too large; split it into 1 occupied and 1 free segment.
                // We have to adjust segment sizes nearby to allow accurate traversal.

                if (_directionForward)
                {
                    if (targetIndex + header.BlockCount < _blockCount)
                    {
                        ref IndexEntry nextEntry = ref _index[targetIndex + header.BlockCount];
                        nextEntry = nextEntry with { BlockCountPrev = header.BlockCount - blockCount };
                    }

                    var leftoverEntry = new IndexEntry() { BlockCount = header.BlockCount - blockCount, BlockCountPrev = blockCount };
                    _index[targetIndex + blockCount] = leftoverEntry;

                    header = header with { Occupied = true, BlockCount = blockCount, Tag = tag };

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
                        ref IndexEntry nextEntry = ref _index[targetIndex + header.BlockCount];
                        nextEntry = nextEntry with { BlockCountPrev = blockCount };
                    }

                    var leftoverEntry = new IndexEntry() { BlockCount = header.BlockCount - blockCount, BlockCountPrev = header.BlockCountPrev };
                    _index[targetIndex] = leftoverEntry;

                    targetIndex += leftoverEntry.BlockCount;
                    _index[targetIndex] = new IndexEntry { Occupied = true, BlockCount = blockCount, BlockCountPrev = leftoverEntry.BlockCount, Tag = tag };

                    _freeBlockBalance -= blockCount;
                }
            }
            else
            {
                header = header with { Occupied = true, Tag = tag };

                if (AdvanceIndex() == false)
                {
                    segment = default;
                    return false;
                }
            }

            Allocations++;
            Used += blockCount * _blockLength;

            segment = new NativeMemorySegment<TSeg, TTag>(_pElems, _pElems + targetIndex * _blockLength, blockCount * _blockLength, tag);
            return true;
        }
    }

    public bool TryReturn(NativeMemorySegment<TSeg, TTag> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<TSeg, TTag>));

        long index = segment.PSegment - _pElems;

        // Convert to block space (divide length by block size).
        long blockIndex = index / _blockLength;
        int blockCount = (int)(segment.Length / _blockLength);

        ref IndexEntry header = ref _index[blockIndex];

        if (header.BlockCount != blockCount)
        {
            return false;
        }

        header = header with { Occupied = false };

        Allocations--;
        Used -= blockCount * _blockLength;

        if (blockIndex >= _currentIndex)
        {
            _freeBlockBalance += blockCount;
        }
        else
        {
            _freeBlockBalance -= blockCount;
        }

        // While we're here, see if we can combine the nearby free segments with this one. //
        long rangeStart = blockIndex;
        int rangeLength = blockCount;

        var nextIndex = blockIndex + header.BlockCount;

        // Combine with next free segments
        while (nextIndex < _blockCount)
        {
            ref IndexEntry nextHeader = ref _index[nextIndex];

            if (nextHeader.Occupied || header.BlockCount + nextHeader.BlockCount > int.MaxValue)
            {
                break;
            }

            header = header with { BlockCount = header.BlockCount + nextHeader.BlockCount };

            rangeLength = header.BlockCount;

            nextIndex += nextHeader.BlockCount;
        }

        // Combine with previous free segments
        var prevPrevIndex = blockIndex;
        var prevIndex = blockIndex - header.BlockCountPrev;

        while (prevIndex >= 0 && prevIndex != prevPrevIndex)
        {
            ref IndexEntry prevHeader = ref _index[prevIndex];

            if (prevHeader.Occupied)
            {
                break;
            }

            ref IndexEntry nextHeader = ref _index[prevIndex + prevHeader.BlockCount];

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
        if (rangeLength != blockCount && rangeStart + rangeLength < _blockCount)
        {
            ref IndexEntry nextHeader = ref _index[rangeStart + rangeLength];
            nextHeader = nextHeader with { BlockCountPrev = rangeLength };
        }

        // If we are in the middle of a larger combined free segment, then move the head.
        if (_currentIndex > rangeStart && _currentIndex < rangeStart + rangeLength)
        {
            _freeBlockBalance += (_currentIndex - rangeStart) << 1;
            _currentIndex = rangeStart;
        }

        return true;
    }

    public void Clear()
    {
        Allocations = 0;
        Used = 0;
        _currentIndex = 0;
        _freeBlockBalance = 0;

        InitIndexes();
    }

    public IEnumerator<NativeMemorySegment<TSeg, TTag>> GetEnumerator() =>
        GetOccupiedSegments().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<NativeMemorySegment<TSeg, TTag>> GetOccupiedSegments()
    {
        long index = _currentIndex;

        IndexEntry GetEntry() => _index[index];

        NativeMemorySegment<TSeg, TTag> GenerateSegment(long blockCount, TTag tag) =>
            new NativeMemorySegment<TSeg, TTag>(_pElems, _pElems + index * _blockLength, blockCount * _blockLength, tag);

        // Iterate backward from current head
        var entry = GetEntry();

        index -= entry.BlockCountPrev;

        while (index > 0 && entry.BlockCountPrev != 0)
        {
            entry = GetEntry();

            if (entry.Occupied == true)
            {
                yield return GenerateSegment(entry.BlockCount, entry.Tag);
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
                yield return GenerateSegment(entry.BlockCount, entry.Tag);
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

            SuballocatorTable<TSeg, TTag>.Deregister(this);

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
                GC.RemoveMemoryPressure(Length * Unsafe.SizeOf<TSeg>());
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
        private readonly TTag _tag;

        public bool Occupied { get => (_general1 & 0x10000000u) != 0; init => _general1 = value ? (_general1 | 0x10000000u) : (_general1 & 0xEFFFFFFFu); }
        public int BlockCount { get => (int)(_general1 & 0xEFFFFFFFu); init => _general1 = (_general1 & 0x10000000u) | ((uint)value & 0xEFFFFFFFu); }
        public int BlockCountPrev { get => _general2; init => _general2 = value; }
        public TTag Tag { get => _tag; init => _tag = value; }
    }
}
