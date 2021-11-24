using System.Buffers;

namespace Suballocation;

/// <summary>
/// A suballocator that returns the free segment at the top of the used stack of segments.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
public unsafe sealed class FixedStackSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private bool _disposed;

    public FixedStackSuballocator(long length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

        CapacityLength = length;

        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;
    }

    public FixedStackSuballocator(T* pData, long length)
    {
        if (pData == null) throw new ArgumentNullException(nameof(pData));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

        CapacityLength = length;

        _pElems = pData;
    }

    public FixedStackSuballocator(Memory<T> data)
    {
        CapacityLength = data.Length;

        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;
    }

    public long UsedBytes => UsedLength * Unsafe.SizeOf<T>();

    public long CapacityBytes => CapacityLength * Unsafe.SizeOf<T>();

    public long Allocations { get; private set; }

    public long UsedLength { get; private set; }

    public long CapacityLength { get; init; }

    public T* PElems => _pElems;

    public byte* PBytes => (byte*)_pElems;

    public NativeMemorySegmentResource<T> RentResource(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FixedStackSuballocator<T>));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length must be >= 1.");

        var rawSegment = Alloc(length);

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
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length must be >= 1.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index, rawSegment.Length);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FixedStackSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Index, long Length) Alloc(long length)
    {
        if (UsedLength + length > CapacityLength)
        {
            throw new OutOfMemoryException();
        }

        long index = UsedLength;

        Allocations++;
        UsedLength += length;

        return new(index, length);
    }

    private unsafe void Free(long index, long length)
    {
        if (UsedLength == 0)
        {
            throw new ArgumentException($"No rented segments found.");
        }

        if (index + length != UsedLength)
        {
            throw new ArgumentException($"Returned segment+length is not from the top of the stack.");
        }

        Allocations--;
        UsedLength -= length;
    }

    public void Clear()
    {
        UsedLength = 0;
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
