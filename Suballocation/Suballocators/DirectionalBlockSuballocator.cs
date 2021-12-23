using Suballocation.Collections;
using System.Buffers;
using System.Collections;

namespace Suballocation.Suballocators;

/// <summary>
/// A suballocator that uses a heuristic to determine the direction in which to search for the next segment to rent.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
/// <typeparam name="TTag">Type to be tied to each segment, as a separate entity from the segment contents.</typeparam>
public unsafe class DirectionalBlockSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
    private readonly BigArray<IndexEntry> _index;
    private readonly long _blockLength;
    private readonly long _blockCount;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private readonly IDirectionStrategy _directionStrategy;
    private long _freeBlockBalance;
    private long _currentIndex;
    private bool _directionForward = true;
    private bool _disposed;

    /// <summary>Creates a suballocator instance and allocates a buffer of the specified length.</summary>
    /// <param name="length">Element length of the buffer to allocate.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <param name="directionStrategy">Optional strategy to use at every rental attempt to determin which direction of the buffer to search. If null, the default strategy is used.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public DirectionalBlockSuballocator(long length, long blockLength = 1, IDirectionStrategy directionStrategy = null!)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _directionStrategy = directionStrategy ?? new DefaultDirectionStrategy();
        Length = length;
        _blockLength = blockLength;
        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _index = new BigArray<IndexEntry>(_blockCount);
        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        GC.AddMemoryPressure(length * Unsafe.SizeOf<T>());
        _privatelyOwned = true;

        InitIndexes();

        SuballocatorTable<T>.Register(this);
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="pElems">A pointer to a pinned memory buffer to use as the backing buffer for this suballocator.</param>
    /// <param name="length">Element length of the given memory buffer.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <param name="directionStrategy">Optional strategy to use at every rental attempt to determin which direction of the buffer to search. If null, the default strategy is used.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public DirectionalBlockSuballocator(T* pElems, long length, long blockLength = 1, IDirectionStrategy directionStrategy = null!)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _directionStrategy = directionStrategy ?? new DefaultDirectionStrategy();
        Length = length;
        _blockLength = blockLength;
        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _index = new BigArray<IndexEntry>(_blockCount);
        _pElems = pElems;

        InitIndexes();

        SuballocatorTable<T>.Register(this);
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="data">A region of memory to use as the backing buffer for this suballocator.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <param name="directionStrategy">Optional strategy to use at every rental attempt to determin which direction of the buffer to search. If null, the default strategy is used.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public DirectionalBlockSuballocator(Memory<T> data, long blockLength = 1, IDirectionStrategy directionStrategy = null!)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _directionStrategy = directionStrategy ?? new DefaultDirectionStrategy();
        Length = data.Length;
        _blockLength = blockLength;
        _blockCount = data.Length / blockLength;
        if (data.Length % blockLength > 0) _blockCount++;
        _index = new BigArray<IndexEntry>(_blockCount);
        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;

        InitIndexes();

        SuballocatorTable<T>.Register(this);
    }

    public long UsedBytes => Used * Unsafe.SizeOf<T>();

    public long LengthBytes => Length * Unsafe.SizeOf<T>();

    public long FreeBytes { get => LengthBytes - UsedBytes; }

    public long Allocations { get; private set; }

    public long Used { get; private set; }

    public long Length { get; init; }

    public long Free { get => Length - Used; }

    public T* PElems => _pElems;

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

    public bool TryRent(long length, out T* segmentPtr, out long lengthActual)
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

        // Decide which direction to search next.
        bool directionForwardPrev = _directionForward;
        _directionForward = false;

        var headOffsetFromCenter = (((_currentIndex + 1) / (double)_blockCount) - .5) * 2;
        var freeBalance = _freeBlockBalance / (double)_blockCount;
        var lastDirection = directionForwardPrev ? 1 : -1;

        _directionForward = _directionStrategy.GetSearchDirection(freeBalance, headOffsetFromCenter, lastDirection);

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
                    segmentPtr = default;
                    lengthActual = 0;
                    return false;
                }

                continue;
            }

            if (header.BlockCount < blockCount)
            {
                if (AdvanceIndex() == false)
                {
                    segmentPtr = default;
                    lengthActual = 0;
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

                    header = header with { Occupied = true, BlockCount = blockCount };

                    _freeBlockBalance -= blockCount;

                    if (AdvanceIndex() == false)
                    {
                        segmentPtr = default;
                        lengthActual = 0;
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
                    _index[targetIndex] = new IndexEntry { Occupied = true, BlockCount = blockCount, BlockCountPrev = leftoverEntry.BlockCount };

                    _freeBlockBalance -= blockCount;
                }
            }
            else
            {
                header = header with { Occupied = true };

                if (AdvanceIndex() == false)
                {
                    segmentPtr = default;
                    lengthActual = 0;
                    return false;
                }
            }

            Allocations++;
            Used += blockCount * _blockLength;

            segmentPtr = _pElems + targetIndex * _blockLength;
            lengthActual = blockCount * _blockLength;
            return true;
        }
    }

    public unsafe long GetSegmentLengthBytes(byte* segmentPtr)
    {
        return GetSegmentLength((T*)segmentPtr) * Unsafe.SizeOf<T>();
    }

    public unsafe long GetSegmentLength(T* segmentPtr)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        long index = segmentPtr - _pElems;

        long blockIndex = index / _blockLength;

        ref IndexEntry header = ref _index[blockIndex];

        if (header.Occupied == false)
        {
            throw new InvalidOperationException($"Attempt to get size of unrented segment.");
        }

        return header.BlockCount * _blockLength;
    }

    unsafe long ISuballocator.Return(byte* segmentPtr)
    {
        return Return((T*)segmentPtr) * Unsafe.SizeOf<T>();
    }

    public unsafe long Return(T* segmentPtr)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));

        long index = segmentPtr - _pElems;

        long blockIndex = index / _blockLength;

        ref IndexEntry header = ref _index[blockIndex];

        if (header.Occupied == false)
        {
            throw new InvalidOperationException($"Attempt to return unrented segment.");
        }

        var blockCount = header.BlockCount;

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

        return blockCount * _blockLength;
    }

    public void Clear()
    {
        Allocations = 0;
        Used = 0;
        _currentIndex = 0;
        _freeBlockBalance = 0;

        InitIndexes();
    }

    public IEnumerator<(IntPtr SegmentPtr, long Length)> GetEnumerator() =>
        GetOccupiedSegments().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<(IntPtr SegmentPtr, long Length)> GetOccupiedSegments()
    {
        long index = _currentIndex;

        IndexEntry GetEntry() => _index[index];

        (IntPtr SegmentPtr, long Length) GenerateSegment(long blockCount) =>
            ((IntPtr)(_pElems + index * _blockLength), blockCount * _blockLength);

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

            SuballocatorTable<T>.Deregister(this);

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
                GC.RemoveMemoryPressure(Length * Unsafe.SizeOf<T>());
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

    public interface IDirectionStrategy
    {
        /// <summary>
        /// Returns a value indicating which direction in the buffer (higher or lower address) to search for the next free segment for rental.
        /// </summary>
        /// <param name="freeBalance">The balance, from -1 to 1, of free space relative to the current buffer read offset. If 1, then all free space is at a higher address; -1 if lower.</param>
        /// <param name="headOffsetFromCenter">The current buffer read offset from the buffer center, normalized to the range: -1 to 1.</param>
        /// <param name="lastDirection">The direction of the latest movement of the buffer reader. This value is either -1 (moved down in address) or 1 (moved up in address).</param>
        /// <returns>True - indicating to search higher addresses, or false - indicating to search lower addresses, relative to the buffer read offset.</returns>
        bool GetSearchDirection(double freeBalance, double headOffsetFromCenter, double lastDirection);
    }

    public class DefaultDirectionStrategy : IDirectionStrategy
    {
        public bool GetSearchDirection(double freeBalance, double headOffsetFromCenter, double lastDirection)
        {
            return freeBalance * 1.0 + headOffsetFromCenter * 0.0 + lastDirection * 0.3 >= 0;
        }
    }
}
