﻿using Suballocation.Collections;
using System.Buffers;
using System.Numerics;

namespace Suballocation.Suballocators;

// TODO: Improved buddy system (https://cs.au.dk/~gerth/papers/actainformatica05.pdf)

public static class BuddySuballocator
{
    /// <summary>Returns the smallest and safest buffer length required to store the given amount of data without requiring defragmentation.</summary>
    /// <param name="count">The number of elements you want to be able to store in a buddy system.</param>
    /// <returns>The length required to guarantee storage.</returns>
    public static long GetBuddyAllocatorLengthFor(long count)
    {
        // Sharath R. Cholleti, "Storage Allocation in Bounded Time"
        // M(log M + 1)/2 where M = count
        // or
        // M(log n + 2)/2 where n = max block length
        return (count * ((long)Math.Log2(count) + 1)) >> 1;
    }
}

/// <summary>
/// Provides a Buddy Allocator on top of a fixed native buffer.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
public unsafe class BuddySuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    public long MinBlockLength;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private BigArray<BlockHeader> _index;
    private T* _pElems;
    private long _maxBlockLength;
    private long _indexLength;
    private long _freeBlockIndexesFlags;
    private long[] _freeBlockIndexesStart = null!;
    private bool _disposed;

    /// <summary>Creates a BuddyAllocator instance and allocates a buffer of the specified length.</summary>
    /// <param name="length">Element length of the buffer to allocate.</param>
    /// <param name="minBlockLength">Element length of the smallest desired block size used internally. If not a power of 2, it will be rounded up to the nearest power of 2.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public BuddySuballocator(long length, long minBlockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");

        _index = null!;
        Length = length;
        _privatelyOwned = true;
        
        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        GC.AddMemoryPressure(length * Unsafe.SizeOf<T>());

        Init(minBlockLength);
    }

    /// <summary>Creates a BuddyAllocator instance using a preallocated backing buffer.</summary>
    /// <param name="pElems">A pointer to a pinned memory buffer to use as the backing buffer for this suballocator.</param>
    /// <param name="length">Element length of the given memory buffer.</param>
    /// <param name="minBlockLength">Element length of the smallest desired block size used internally. If not a power of 2, it will be rounded up to the nearest power of 2.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public BuddySuballocator(T* pElems, long length, long minBlockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));

        _index = null!;
        Length = length;

        _pElems = pElems;

        Init(minBlockLength);
    }

    /// <summary>Creates a BuddyAllocator instance using a preallocated backing buffer.</summary>
    /// <param name="data">A region of memory to use as the backing buffer for this suballocator.</param>
    /// <param name="minBlockLength">Element length of the smallest desired block size used internally. If not a power of 2, it will be rounded up to the nearest power of 2.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public BuddySuballocator(Memory<T> data, long minBlockLength = 1)
    {
        if (data.Length == 0) throw new ArgumentOutOfRangeException(nameof(data), $"Cannot allocate a backing buffer of size <= 0.");
        if (minBlockLength > (uint)data.Length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(data.Length)}.");

        _index = null!;
        Length = (long)data.Length;
        _memoryHandle = data.Pin();

        _pElems = (T*)_memoryHandle.Pointer;

        Init(minBlockLength);
    }

    public long BlocksUsed { get; private set; }

    public long UsedBytes => Used * (long)Unsafe.SizeOf<T>();

    public long LengthBytes => Length * (long)Unsafe.SizeOf<T>();

    public long FreeBytes { get => LengthBytes - UsedBytes; }

    public long Allocations { get; private set; }

    public long Used { get => BlocksUsed * MinBlockLength; }

    public long Length { get; init; }

    public long Free { get => Length - Used; }

    public T* PElems => _pElems;

    public byte* PBytes => (byte*)_pElems;

    /// <summary>Common construction logic.</summary>
    private void Init(long minBlockLength)
    {
        MinBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)minBlockLength);

        _indexLength = Length >> BitOperations.Log2((ulong)MinBlockLength);
        _index = new BigArray<BlockHeader>(_indexLength);
        _maxBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)_indexLength);
        _freeBlockIndexesStart = new long[BitOperations.Log2((ulong)_maxBlockLength) + 1];

        InitIndexes();

        SuballocatorTable<T>.Register(this);
    }

    /// <summary>Index initialization logic.</summary>
    private void InitIndexes()
    {
        // Empty the free block index enties.
        _freeBlockIndexesStart.AsSpan().Fill(long.MaxValue);
        _freeBlockIndexesFlags = 0;

        long index = 0;

        // Insert free blocks, as large as possible.
        for (long blockLength = _maxBlockLength; blockLength > 0; blockLength >>= 1)
        {
            if (blockLength > _indexLength - index)
            {
                continue;
            }

            var blockLengthLog = BitOperations.Log2((ulong)blockLength);

            ref BlockHeader header = ref _index[index];

            header = header with { Valid = true, Occupied = false, BlockCountLog = blockLengthLog, NextFree = long.MaxValue, PreviousFree = long.MaxValue };

            _freeBlockIndexesStart[blockLengthLog] = index;
            _freeBlockIndexesFlags |= blockLength;

            index += header.BlockCount;
        }
    }

    public bool TryClone(byte* sourceSegmentPtr, out byte* destinationSegmentPtr, out long lengthActual)
    {
        if(TryClone((T*)sourceSegmentPtr, out var unitDestinationPtr, out lengthActual) == false)
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

        // Convert to block space (divide length by block size).
        long offset = sourceSegmentPtr - _pElems;

        long freeBlockIndexIndex = offset / MinBlockLength;

        if (freeBlockIndexIndex < 0 || freeBlockIndexIndex >= _indexLength)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSegmentPtr));
        }

        ref BlockHeader header = ref _index[freeBlockIndexIndex];

        if (header.Occupied == false || header.Valid == false)
        {
            throw new InvalidOperationException($"Attempt to clone an unrented segment.");
        }

        long length = header.BlockCount * MinBlockLength;

        if (TryRent(length, out destinationSegmentPtr, out lengthActual) == false)
        {
            return false;
        }

        Debug.Assert(length == lengthActual);

        Buffer.MemoryCopy(sourceSegmentPtr, destinationSegmentPtr, lengthActual * Unsafe.SizeOf<T>(), length * Unsafe.SizeOf<T>());

        return true;
    }

    bool ISuballocator.TryRent(long length, out byte* segmentPtr, out long lengthActual)
    {
        var unitLength = length / Unsafe.SizeOf<T>();
        if(unitLength * Unsafe.SizeOf<T>() != length)
        {
            unitLength++;
        }

        if (TryRent(unitLength, out var unitSegmentPtr, out lengthActual) == false)
        {
            segmentPtr = default;
            return false;
        }

        segmentPtr = (byte*)unitSegmentPtr;
        lengthActual *= Unsafe.SizeOf<T>();
        return true;
    }

    public bool TryRent(long length, out T* segmentPtr, out long lengthActual)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length must be >= 1.");

        // Convert to block space (divide length by block size), then round up to a power of 2, since all allocations must be a power of 2.
        long blockCount = (long)BitOperations.RoundUpToPowerOf2((ulong)length) >> BitOperations.Log2((ulong)MinBlockLength);
        if (blockCount == 0)
        {
            blockCount = 1;
        }

        // The free block index flags is a bit field where each index corresponds to the Log2 of the blockCount.
        // Therefore we can use it to quickly find the smallest index/size that will accomodate this block size.
        int minFreeBlockIndex = BitOperations.Log2((ulong)blockCount);

        long mask = ~(blockCount - 1);

        long matchingBlockLengths = mask & _freeBlockIndexesFlags;

        if (matchingBlockLengths == 0)
        {
            // No free blocks with a length large enough...
            segmentPtr = default;
            lengthActual = 0;
            return false;
        }

        // We know there are free block(s) at a certain length. Grab the smallest/first one we find in the chain.
        int freeBlockIndex = BitOperations.TrailingZeroCount((ulong)matchingBlockLengths);

        long freeBlockIndexIndex = _freeBlockIndexesStart[freeBlockIndex];

        ref BlockHeader header = ref _index[freeBlockIndexIndex];

        long splitLength = header.BlockCount;

        Debug.Assert(header.Occupied == false && header.Valid == true);

        RemoveFromFreeList(ref header);

        // If the found block length is larger than the minimum we require, then recursively split it in half
        // until we have the minimum length.
        for (int i = minFreeBlockIndex; i < freeBlockIndex; i++)
        {
            splitLength = splitLength >> 1;

            long splitIndex = freeBlockIndexIndex + splitLength;

            ref BlockHeader header2 = ref _index[splitIndex];

            header2 = header2 with { Valid = true, Occupied = false, BlockCount = splitLength, NextFree = _freeBlockIndexesStart[BitOperations.Log2((ulong)splitLength)], PreviousFree = long.MaxValue };

            _freeBlockIndexesStart[header2.BlockCountLog] = splitIndex;
            _freeBlockIndexesFlags |= header2.BlockCount;

            if (header2.NextFree != long.MaxValue)
            {
                ref BlockHeader nextHeader = ref _index[header2.NextFree];
                nextHeader = nextHeader with { PreviousFree = splitIndex };
            }
        }

        // Occupy the final block segment.
        header = header with { Occupied = true, BlockCountLog = minFreeBlockIndex, NextFree = long.MaxValue, PreviousFree = long.MaxValue };

        Allocations++;
        BlocksUsed += header.BlockCount;

        segmentPtr = _pElems + freeBlockIndexIndex * MinBlockLength;
        lengthActual = header.BlockCount * MinBlockLength;
        return true;
    }

    public unsafe long GetSegmentLengthBytes(byte* segmentPtr)
    {
        return GetSegmentLength((T*)segmentPtr) * Unsafe.SizeOf<T>();
    }

    public unsafe long GetSegmentLength(T* segmentPtr)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        // Convert to block space (divide length by block size).
        long offset = segmentPtr - _pElems;

        long freeBlockIndexIndex = offset / MinBlockLength;

        if (freeBlockIndexIndex < 0 || freeBlockIndexIndex >= _indexLength)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentPtr));
        }

        ref BlockHeader header = ref _index[freeBlockIndexIndex];

        if (header.Occupied == false || header.Valid == false)
        {
            throw new InvalidOperationException($"Attempt to get size of unrented segment.");
        }

        return header.BlockCount * MinBlockLength;
    }

    unsafe long ISuballocator.Return(byte* segmentPtr)
    {
        return Return((T*)segmentPtr) * Unsafe.SizeOf<T>();
    }

    public unsafe long Return(T* segmentPtr)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BuddySuballocator<T>));

        // Convert to block space (divide length by block size).
        long offset = segmentPtr - _pElems;

        long freeBlockIndexIndex = offset / MinBlockLength;

        if (freeBlockIndexIndex < 0 || freeBlockIndexIndex >= _indexLength)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentPtr));
        }

        ref BlockHeader header = ref _index[freeBlockIndexIndex];

        if (header.Occupied == false || header.Valid == false)
        {
            throw new InvalidOperationException($"Attempt to return unrented segment.");
        }

        header = header with { Valid = false };

        Allocations--;
        BlocksUsed -= header.BlockCount;
        long length = header.BlockCount * MinBlockLength;

        // If the returned block has a 'buddy' block next to it that is also free, then combine the two (recursively).
        Combine(freeBlockIndexIndex, header.BlockCountLog);

        void Combine(long blockIndexIndex, int lengthLog)
        {
            ref BlockHeader header = ref _index[blockIndexIndex];

            var buddyBlockIndexIndex = blockIndexIndex ^ (1L << lengthLog);

            if (buddyBlockIndexIndex >= _indexLength || _index[buddyBlockIndexIndex].Occupied || _index[buddyBlockIndexIndex].Valid == false || _index[buddyBlockIndexIndex].BlockCountLog != lengthLog)
            {
                // No buddy / the end of the buffer.
                var nextFree = _freeBlockIndexesStart[lengthLog] == blockIndexIndex ? long.MaxValue : _freeBlockIndexesStart[lengthLog];
                header = header with { Valid = true, Occupied = false, BlockCountLog = lengthLog, NextFree = nextFree, PreviousFree = long.MaxValue };
                _freeBlockIndexesStart[lengthLog] = blockIndexIndex;
                _freeBlockIndexesFlags |= 1L << lengthLog;

                if (nextFree != long.MaxValue)
                {
                    ref BlockHeader nextHeader = ref _index[header.NextFree];
                    nextHeader = nextHeader with { PreviousFree = blockIndexIndex };
                }

                return;
            }

            ref var buddyHeader = ref _index[buddyBlockIndexIndex];

            buddyHeader = buddyHeader with { Valid = false };

            if (buddyBlockIndexIndex < blockIndexIndex)
            {
                blockIndexIndex = buddyBlockIndexIndex;
            }

            RemoveFromFreeList(ref buddyHeader);

            Combine(blockIndexIndex, lengthLog + 1);
        }

        return length;
    }

    /// <summary>Free blocks are changed to each other, grouped by size. This removes a block from a chain and connects the chain.</summary>
    private void RemoveFromFreeList(ref BlockHeader header)
    {
        if (header.NextFree != long.MaxValue)
        {
            ref BlockHeader nextHeader = ref _index[header.NextFree];

            nextHeader = nextHeader with { PreviousFree = header.PreviousFree };

            if (header.PreviousFree != long.MaxValue)
            {
                ref BlockHeader prevHeader = ref _index[header.PreviousFree];

                prevHeader = prevHeader with { NextFree = header.NextFree };

                nextHeader = nextHeader with { PreviousFree = header.PreviousFree };
            }
            else
            {
                nextHeader = nextHeader with { PreviousFree = long.MaxValue };

                _freeBlockIndexesStart[header.BlockCountLog] = header.NextFree;
            }
        }
        else if (header.PreviousFree != long.MaxValue)
        {
            ref BlockHeader prevHeader = ref _index[header.PreviousFree];

            prevHeader = prevHeader with { NextFree = long.MaxValue };
        }
        else
        {
            _freeBlockIndexesStart[header.BlockCountLog] = long.MaxValue;
            _freeBlockIndexesFlags &= ~header.BlockCount;
        }
    }

    public void Clear()
    {
        Allocations = 0;
        BlocksUsed = 0;

        _index.Clear();

        InitIndexes();
    }

    public IEnumerator<(IntPtr SegmentPtr, long Length)> GetEnumerator() =>
        GetOccupiedSegments().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<(IntPtr SegmentPtr, long Length)> GetOccupiedSegments()
    {
        long index = 0;

        while (index < _indexLength)
        {
            BlockHeader GetHeader() => _index[index];

            BlockHeader header = GetHeader();

            if(header.Valid == false)
            {
                index++;
                continue;
            }

            (IntPtr SegmentPtr, long Length) GenerateSegment() =>
                ((IntPtr)(_pElems + index * MinBlockLength), header.BlockCount * MinBlockLength);

            if(header.Occupied == true)
            {
                yield return GenerateSegment();
            }

            index += header.BlockCount;
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            SuballocatorTable<T>.Deregister(this);

            _index = null!;

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
                GC.RemoveMemoryPressure(Length * Unsafe.SizeOf<T>());
            }

            _disposed = true;
        }
    }

    ~BuddySuballocator()
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
        private readonly long _prevFree;
        private readonly long _nextFree;
        private readonly byte _infoByte;

        public bool Valid { get => (_infoByte & 0b1000_0000) != 0; init => _infoByte = value ? (byte)(_infoByte | 0b1000_0000) : (byte)(_infoByte & 0b0111_1111); }
        public bool Occupied { get => (_infoByte & 0b0100_0000) != 0; init => _infoByte = value ? (byte)(_infoByte | 0b0100_0000) : (byte)(_infoByte & 0b1011_1111); }
        public int BlockCountLog { get => (_infoByte & 0b0011_1111); init => _infoByte = (byte)((_infoByte & 0b1100_0000) | (value & 0b0011_1111)); }
        public long BlockCount
        {
            get => (1L << BlockCountLog);
            init
            {
                if (BitOperations.IsPow2(value) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(BlockCount), "Must be a power of 2.");
                }

                BlockCountLog = BitOperations.Log2((ulong)value);
            }
        }

        public long PreviousFree { get => _prevFree; init => _prevFree = value; }
        public long NextFree { get => _nextFree; init => _nextFree = value; }
    }
}
