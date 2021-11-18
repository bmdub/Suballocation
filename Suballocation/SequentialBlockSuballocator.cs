﻿using Suballocation.Collections;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

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

    public SequentialBlockSuballocator(long length, long blockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        LengthTotal = length;

        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
        }
    }

    public SequentialBlockSuballocator(T* pElems, long length, long blockLength = 1)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        LengthTotal = length;

        _blockCount = length / blockLength;
        if (length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
        _pElems = pElems;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
        }
    }

    public SequentialBlockSuballocator(Memory<T> data, long blockLength = 1)
    {
        if (blockLength <= 0 || blockLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0 and less than Int32.Max.");

        _blockLength = blockLength;
        LengthTotal = data.Length;

        _blockCount = data.Length / blockLength;
        if (data.Length % blockLength > 0) _blockCount++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_blockCount * sizeof(IndexEntry)));
        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
        }
    }

    public long LengthBytesUsed => LengthUsed * Unsafe.SizeOf<T>();

    public long LengthBytesTotal => LengthTotal * Unsafe.SizeOf<T>();

    public long Allocations { get; private set; }

    public long LengthUsed { get; private set; }

    public long LengthTotal { get; init; }

    public T* PElems => _pElems;

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

        long blockIndex = _lastIndex;

        for (; ; )
        {
            ref IndexEntry header = ref _pIndex[blockIndex];

            if (header.Occupied == false)
            {
                var nextIndex = blockIndex + header.BlockCount;

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
                    if (header.BlockCount > blockCount)
                    {
                        var leftoverEntry = new IndexEntry() { BlockCount = header.BlockCount - blockCount };
                        _pIndex[blockIndex + blockCount] = leftoverEntry;

                        header = header with { BlockCount = blockCount };
                    }

                    header = header with { Occupied = true };

                    Allocations++;
                    LengthUsed += length;

                    _lastIndex = blockIndex;

                    return new(blockIndex * _blockLength, blockCount * _blockLength);
                }
            }

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
        long blockIndex = index / _blockLength;
        long blockLength = length / _blockLength;

        ref IndexEntry header = ref _pIndex[blockIndex];

        if (header.BlockCount != blockLength)
        {
            throw new ArgumentException($"No rented segment found at index {index} with length {length}.");
        }

        header = header with { Occupied = false };

        Allocations--;
        LengthUsed -= length;
    }

    public void Clear()
    {
        Allocations = 0;
        LengthUsed = 0;
        _lastIndex = 0;

        for (long i = 0; i < _blockCount; i += int.MaxValue)
        {
            _pIndex[i] = new IndexEntry() { BlockCount = Math.Min(int.MaxValue, (int)(_blockCount - i)) };
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
