using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

public unsafe sealed class FixedStackSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private readonly long _segmentLength;
    private bool _disposed;

    public FixedStackSuballocator(long length, long segmentLength)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

        LengthTotal = length;
        _segmentLength = segmentLength;

        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;
    }

    public FixedStackSuballocator(T* pData, long length, long segmentLength)
    {
        if (pData == null) throw new ArgumentNullException(nameof(pData));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

        LengthTotal = length;
        _segmentLength = segmentLength;

        _pElems = pData;
    }

    public FixedStackSuballocator(Memory<T> data, long segmentLength)
    {
        LengthTotal = data.Length;
        _segmentLength = segmentLength;

        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;
    }

    public long SizeUsed => LengthUsed * Unsafe.SizeOf<T>();

    public long SizeTotal => LengthTotal * Unsafe.SizeOf<T>();

    public long Allocations { get; private set; }

    public long LengthUsed { get; private set; }

    public long LengthTotal { get; init; }

    public T* PElems => _pElems;

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FixedStackSuballocator<T>));
        if (length != _segmentLength) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length must be user-defined length ({_segmentLength}).");

        var rawSegment = Alloc(_segmentLength);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index, rawSegment.Length);
    }

    public void ReturnResource(NativeMemorySegmentResource<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FixedStackSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    public NativeMemorySegment<T> Rent(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FixedStackSuballocator<T>));
        if (length != _segmentLength) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length must be user-defined length ({_segmentLength}).");

        var rawSegment = Alloc(_segmentLength);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index, rawSegment.Length);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FixedStackSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Index, long Length) Alloc(long length)
    {
        if (LengthUsed + length > LengthTotal)
        {
            throw new OutOfMemoryException();
        }

        long index = LengthUsed;

        Allocations++;
        LengthUsed += length;

        return new(index, length);
    }

    private unsafe void Free(long index, long length)
    {
        if (LengthUsed < _segmentLength)
        {
            throw new ArgumentException($"No rented segments found.");
        }

        if (length != _segmentLength)
        {
            throw new ArgumentException($"Returned segment does not have expected length ({_segmentLength:N0}).");
        }

        Allocations--;
        LengthUsed -= length;
    }

    public void Clear()
    {
        LengthUsed = 0;
        Allocations = 0;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
            }

            _disposed = true;
        }
    }

    ~FixedStackSuballocator()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
