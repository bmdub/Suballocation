using Suballocation.Collections;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

public unsafe sealed class SequentialBlockSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly long _blockLength;
    private readonly T* _pElems;
    private readonly IndexEntry* _pIndex;
    private readonly long _indexLength;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private long _lastIndex;
    private bool _disposed;

    public SequentialBlockSuballocator(long length, long blockLength = 1)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0.");

        _blockLength = blockLength;
        LengthTotal = length;

        _indexLength = length / blockLength;
        if (length % blockLength > 0) _indexLength++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_indexLength * sizeof(IndexEntry)));
        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;

        _pIndex[0] = new IndexEntry() { BlockLength = _indexLength };
    }

    public SequentialBlockSuballocator(T* pElems, long length, long blockLength = 1)
    {
        if (pElems == null) throw new ArgumentNullException(nameof(pElems));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Buffer length must be greater than 0.");
        if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0.");

        _blockLength = blockLength;
        LengthTotal = length;

        _indexLength = length / blockLength;
        if (length % blockLength > 0) _indexLength++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_indexLength * sizeof(IndexEntry)));
        _pElems = pElems;

        _pIndex[0] = new IndexEntry() { BlockLength = _indexLength };
    }

    public SequentialBlockSuballocator(Memory<T> data, long blockLength = 1)
    {
        if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Block length must be greater than 0.");

        _blockLength = blockLength;
        LengthTotal = data.Length;

        _indexLength = data.Length / blockLength;
        if (data.Length % blockLength > 0) _indexLength++;
        _pIndex = (IndexEntry*)NativeMemory.Alloc((nuint)(_indexLength * sizeof(IndexEntry)));
        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;

        _pIndex[0] = new IndexEntry() { BlockLength = _indexLength };
    }

    public long SizeUsed => LengthUsed * Unsafe.SizeOf<T>();

    public long SizeTotal => LengthTotal * Unsafe.SizeOf<T>();

    public long Allocations { get; private set; }

    public long LengthUsed { get; private set; }

    public long LengthTotal { get; init; }

    public T* PElems => _pElems;

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index * _blockLength, rawSegment.Length * _blockLength);
    }

    public void ReturnResource(NativeMemorySegmentResource<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    public NativeMemorySegment<T> Rent(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index * _blockLength, rawSegment.Length * _blockLength);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialBlockSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Index, long Length) Alloc(long length)
    {
        if (LengthUsed + length > LengthTotal)
        {
            throw new OutOfMemoryException();
        }

        long blockLength = length / _blockLength;
        if (length % _blockLength > 0)
        {
            blockLength++;
        }

        long index = _lastIndex;

        for (; ; )
        {
            ref IndexEntry header = ref _pIndex[index];

            var nextIndex = index + header.BlockLength;

            if (header.Occupied == false)
            {
                while (nextIndex < _indexLength && nextIndex == index + header.BlockLength)
                {.//todo: move this part to Free()
                    ref IndexEntry nextHeader = ref _pIndex[nextIndex];

                    if(nextHeader.Occupied)
                    {
                        break;
                    }

                    header = header with { BlockLength = header.BlockLength + nextHeader.BlockLength };

                    nextIndex += nextHeader.BlockLength;
                }

                if (header.BlockLength >= blockLength)
                {
                    if (header.BlockLength > blockLength)
                    {
                        var leftoverEntry = new IndexEntry() { BlockLength = header.BlockLength - blockLength };
                        _pIndex[index + blockLength] = leftoverEntry;

                        header = header with { BlockLength = blockLength };
                    }

                    header = header with { Occupied = true };

                    Allocations++;
                    LengthUsed += length;

                    _lastIndex = index;

                    return new(index, blockLength);
                }
            }

            index = nextIndex;
            if (index >= _indexLength)
                index = 0; // Assuming that there is always a segment at 0

            if (index == _lastIndex)
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

        if (header.BlockLength != blockLength)
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
        _pIndex[0] = new IndexEntry() { BlockLength = _indexLength };
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
        private readonly ulong _general;

        public bool Occupied { get => (_general & 0x1000000000000000ul) != 0; init => _general = value ? (_general | 0x1000000000000000ul) : (_general & 0xEFFFFFFFFFFFFFFFul); }
        public long BlockLength { get => (long)(_general & 0xEFFFFFFFFFFFFFFFul); init => _general = (_general & 0x1000000000000000ul) | ((ulong)value & 0xEFFFFFFFFFFFFFFFul); }
    }
}
