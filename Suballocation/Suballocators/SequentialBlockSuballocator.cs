using Suballocation.Collections;
using System.Buffers;

namespace Suballocation.Suballocators;

/// <summary>
/// A sequential-fit suballocator that returns the nearest free next segment that is large enough to fulfill the request.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
public unsafe class SequentialBlockSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
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
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public SequentialBlockSuballocator(T* pElems, long length, long blockLength = 1)
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

        SuballocatorTable<T>.Register(this);
    }

    /// <summary>Creates a suballocator instance using a preallocated backing buffer.</summary>
    /// <param name="data">A region of memory to use as the backing buffer for this suballocator.</param>
    /// <param name="blockLength">Element length of the smallest desired block size used internally for any rented segment.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public SequentialBlockSuballocator(Memory<T> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        Length = data.Length;
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
            _index[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
        }
    }

    public bool TryClone(byte* sourceSegmentPtr, out byte* destinationSegmentPtr, out long lengthActual)
    {
        if (TryClone((T*)sourceSegmentPtr, out var unitDestinationPtr, out lengthActual) == false)
        {
            destinationSegmentPtr = default;
            return false;
        }

        destinationSegmentPtr = (byte*)unitDestinationPtr;
        lengthActual *= Unsafe.SizeOf<T>();
        return true;
    }

    public bool TryClone(T* sourceSegmentPtr, out T* destinationSegmentPtr, out long lengthActual)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        long index = sourceSegmentPtr - _pElems;

        long blockIndex = index / _blockLength;

        ref IndexEntry header = ref _index[blockIndex];

        if (header.Occupied == false)
        {
            throw new InvalidOperationException($"Attempt to clone an unrented segment.");
        }

        long length = header.BlockCount * _blockLength;

        if (TryRent(length, out destinationSegmentPtr, out lengthActual) == false)
        {
            return false;
        }

        Debug.Assert(length == lengthActual);

        Buffer.MemoryCopy(sourceSegmentPtr, destinationSegmentPtr, lengthActual * Unsafe.SizeOf<T>(), length * Unsafe.SizeOf<T>());

        return true;
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

                    header = header with { Occupied = true };

                    Allocations++;
                    Used += blockCount * _blockLength;

                    _lastIndex = blockIndex;

                    segmentPtr = _pElems + blockIndex * _blockLength;        
                    lengthActual = blockCount * _blockLength;
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

        segmentPtr = default;
        lengthActual = 0;
        return false;
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

        header = header with { Occupied = false };

        Allocations--;
        Used -= header.BlockCount * _blockLength;

        return header.BlockCount * _blockLength;
    }

    public void Clear()
    {
        Allocations = 0;
        Used = 0;
        _lastIndex = 0;

        InitIndexes();
    }

    public IEnumerator<(IntPtr SegmentPtr, long Length)> GetEnumerator() =>
        GetOccupiedSegments().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<(IntPtr SegmentPtr, long Length)> GetOccupiedSegments()
    {
        long index = 0;

        IndexEntry GetEntry() => _index[index];

        (IntPtr SegmentPtr, long Length) GenerateSegment(long blockCount) =>
            ((IntPtr)(_pElems + index * _blockLength), blockCount * _blockLength);

        while (index < _blockCount)
        {
            var entry = GetEntry();

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
