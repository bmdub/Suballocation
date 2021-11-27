using System.Buffers;

namespace Suballocation.Suballocators;

/// <summary>
/// A sequential-fit suballocator that returns the nearest free next segment that is large enough to fulfill the request.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
public unsafe sealed class SequentialBlockSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
    private readonly IndexEntry* _pIndex;
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
        CapacityLength = length;

        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
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
    public SequentialBlockSuballocator(T* pElems, long length, long blockLength = 1)
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

        InitIndexes();
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="data">A region of memory to use as the backing buffer for this suballocator.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public SequentialBlockSuballocator(Memory<T> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        CapacityLength = data.Length;

        _blockCount = data.Length / blockLength;
        if (data.Length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
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
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
        }
    }

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
        // Convert to block space (divide length by block size).
        int blockCount = (int)(length / _blockLength);
        if (length * _blockLength != length)
        {
            blockCount++;
        }

        long blockIndex = _lastIndex;

        // Find a large-enough free segment to return.
        for (; ; )
        {
            ref IndexEntry header = ref _pIndex[blockIndex];

            if (header.Occupied == false)
            {
                var nextIndex = blockIndex + header.BlockCount;

                // If the free block is too small, see if we can combine it with the next free one(s).
                while (header.BlockCount < blockCount && nextIndex < _blockCount)
                {
                    ref IndexEntry nextHeader = ref _pIndex[nextIndex];

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
                        _pIndex[blockIndex + blockCount] = leftoverEntry;

                        header = header with { BlockCount = blockCount };
                    }

                    header = header with { Occupied = true };

                    Allocations++;
                    UsedLength += blockCount * _blockLength;

                    _lastIndex = blockIndex;

                    return new(blockIndex * _blockLength, length);
                }
            }

            // Try the next block.
            blockIndex = blockIndex + header.BlockCount;
            if (blockIndex >= _blockCount)
                blockIndex = 0; // Assuming that there is always a segment at 0

            if (blockIndex == _lastIndex)
            {
                // Looped around to initial index position
                break;
            }
        }

        throw new OutOfMemoryException();
    }

    private unsafe void Free(long index, long length)
    {
        // Convert to block space (divide length by block size).
        long blockIndex = index / _blockLength;
        long blockCount = length / _blockLength;
        if (length * _blockLength != length)
        {
            blockCount++;
        }

        ref IndexEntry header = ref _pIndex[blockIndex];

        if (header.BlockCount != blockCount)
        {
            throw new ArgumentException($"No rented segment found at index {index} with length {length}.");
        }

        header = header with { Occupied = false };

        Allocations--;
        UsedLength -= blockCount * _blockLength;
    }

    public void Clear()
    {
        Allocations = 0;
        UsedLength = 0;
        _lastIndex = 0;

        InitIndexes();
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

        public bool Occupied { get => (_general & 0x10000000u) != 0; init => _general = value ? (_general | 0x10000000u) : (_general & 0xEFFFFFFFu); }
        public int BlockCount { get => (int)(_general & 0xEFFFFFFFu); init => _general = (_general & 0x10000000u) | ((uint)value & 0xEFFFFFFFu); }
    }
}
