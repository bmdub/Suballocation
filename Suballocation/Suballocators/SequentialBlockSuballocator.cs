using Suballocation.Collections;
using System.Buffers;

namespace Suballocation.Suballocators;

public unsafe class SequentialBlockSuballocator<TSeg> : SequentialBlockSuballocator<TSeg, EmptyStruct>, ISuballocator<TSeg> where TSeg : unmanaged
{
    public SequentialBlockSuballocator(long length, long blockLength = 1) : base(length, blockLength) { }

    public SequentialBlockSuballocator(TSeg* pElems, long length, long blockLength = 1) : base(pElems, length, blockLength) { }

    public SequentialBlockSuballocator(Memory<TSeg> data, long blockLength = 1) : base(data, blockLength) { }
}

/// <summary>
/// A sequential-fit suballocator that returns the nearest free next segment that is large enough to fulfill the request.
/// </summary>
/// <typeparam name="TSeg">A blittable element type that defines the units to allocate.</typeparam>
/// <typeparam name="TTag">Type to be tied to each segment, as a separate entity from the segment contents.</typeparam>
public unsafe class SequentialBlockSuballocator<TSeg, TTag> : ISuballocator<TSeg, TTag>, IDisposable where TSeg : unmanaged
{
    private readonly TSeg* _pElems;
    private readonly BigArray<IndexEntry> _index;
    private readonly long _blockLength;
    private readonly long _blockCount;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private long _lastIndex;
    private bool _disposed;

    /// <summary>Creates a suballocator instance and allocates a buffer of the specified length.</summary>
    /// <param name="length">Element length of the buffer to allocate.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public SequentialBlockSuballocator(long length, long blockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        Length = length;
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
    public SequentialBlockSuballocator(TSeg* pElems, long length, long blockLength = 1)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        Length = length;
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
    public SequentialBlockSuballocator(Memory<TSeg> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        Length = data.Length;
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
            _index[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
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

        long blockIndex = _lastIndex;

        // Find a large-enough free segment to return.
        for (; ; )
        {
            ref IndexEntry header = ref _index[blockIndex];

            if (header.Occupied == false)
            {
                var nextIndex = blockIndex + header.BlockCount;

                // If the free block is too small, see if we can combine it with the next free one(s).
                while (header.BlockCount < blockCount && nextIndex < _blockCount)
                {
                    ref IndexEntry nextHeader = ref _index[nextIndex];

                    if (nextHeader.Occupied)
                    {
                        break;
                    }

                    header = header with { BlockCount = header.BlockCount + nextHeader.BlockCount };

                    nextIndex += nextHeader.BlockCount;
                }

                if (header.BlockCount >= blockCount)
                {
                    // Block is big enough to use...

                    if (header.BlockCount > blockCount)
                    {
                        // Block is too big; split into 1 occupied and 1 free block.
                        var leftoverEntry = new IndexEntry() { BlockCount = header.BlockCount - blockCount };
                        _index[blockIndex + blockCount] = leftoverEntry;

                        header = header with { BlockCount = blockCount };
                    }

                    header = header with { Occupied = true, Tag = tag };

                    Allocations++;
                    Used += blockCount * _blockLength;

                    _lastIndex = blockIndex;

                    segment = new NativeMemorySegment<TSeg, TTag>(_pElems, _pElems + blockIndex * _blockLength, blockCount * _blockLength, tag);
                    return true;
                }
            }

            // Try the next block.
            blockIndex = blockIndex + header.BlockCount;
            if (blockIndex >= _blockCount)
                blockIndex = 0; // Assuming that there is always a segment at 0

            if (blockIndex == _lastIndex)
            {
                // Looped around to initial index position; we found no available segment of the requested size.
                break;
            }
        }

        segment = default;
        return false;
    }

    public bool TryReturn(NativeMemorySegment<TSeg, TTag> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<TSeg, TTag>));

        long index = segment.PSegment - _pElems;

        // Convert to block space (divide length by block size).
        long blockIndex = index / _blockLength;
        long blockCount = segment.Length / _blockLength;

        ref IndexEntry header = ref _index[blockIndex];

        if (header.BlockCount != blockCount)
        {
            return false;
        }

        header = header with { Occupied = false };

        Allocations--;
        Used -= blockCount * _blockLength;
        return true;
    }

    public void Clear()
    {
        Allocations = 0;
        Used = 0;
        _lastIndex = 0;

        InitIndexes();
    }

    public IEnumerator<NativeMemorySegment<TSeg, TTag>> GetEnumerator() =>
        GetOccupiedSegments().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<NativeMemorySegment<TSeg, TTag>> GetOccupiedSegments()
    {
        long index = 0;

        IndexEntry GetEntry() => _index[index];

        NativeMemorySegment<TSeg, TTag> GenerateSegment(long blockCount, TTag tag) =>
            new NativeMemorySegment<TSeg, TTag>(_pElems, _pElems + index * _blockLength, blockCount * _blockLength, tag);

        while (index < _blockCount)
        {
            var entry = GetEntry();

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

    ~SequentialBlockSuballocator()
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
        private readonly uint _general;
        private readonly TTag _tag;

        public bool Occupied { get => (_general & 0x10000000u) != 0; init => _general = value ? (_general | 0x10000000u) : (_general & 0xEFFFFFFFu); }
        public int BlockCount { get => (int)(_general & 0xEFFFFFFFu); init => _general = (_general & 0x10000000u) | ((uint)value & 0xEFFFFFFFu); }
        public TTag Tag { get => _tag; init => _tag = value; }
    }
}
