using Suballocation.Collections;
using System.Buffers;

namespace Suballocation;

/// <summary>
/// A sequential-fit suballocator that returns the nearest free next segment that is large enough to fulfill the request.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units to allocate.</typeparam>
public unsafe sealed class SequentialFitSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
{
    private readonly T* _pElems;
    private readonly MemoryHandle _memoryHandle;
    private readonly bool _privatelyOwned;
    private readonly NativeBitArray _allocatedIndexes;
    private NativeQueue<IndexEntry> _indexQueue = new();
    private NativeQueue<IndexEntry> _futureQueue = new();
    private bool _disposed;

    public SequentialFitSuballocator(long length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

        CapacityLength = length;
        _allocatedIndexes = new NativeBitArray(length);

        _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
        _privatelyOwned = true;

        _indexQueue.Enqueue(new IndexEntry() { Index = 0, Length = length });
    }

    public SequentialFitSuballocator(T* pData, long length)
    {
        if (pData == null) throw new ArgumentNullException(nameof(pData));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

        CapacityLength = length;
        _allocatedIndexes = new NativeBitArray(length);

        _pElems = pData;

        _indexQueue.Enqueue(new IndexEntry() { Index = 0, Length = length });
    }

    public SequentialFitSuballocator(Memory<T> data)
    {
        CapacityLength = data.Length;
        _allocatedIndexes = new NativeBitArray(data.Length);

        _memoryHandle = data.Pin();
        _pElems = (T*)_memoryHandle.Pointer;

        _indexQueue.Enqueue(new IndexEntry() { Index = 0, Length = data.Length });
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
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialFitSuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index, rawSegment.Length);
    }

    public void ReturnResource(NativeMemorySegmentResource<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialFitSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    public NativeMemorySegment<T> Rent(long length = 1)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialFitSuballocator<T>));
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

        var rawSegment = Alloc(length);

        return new NativeMemorySegment<T>(_pElems + rawSegment.Index, rawSegment.Length);
    }

    public void Return(NativeMemorySegment<T> segment)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SequentialFitSuballocator<T>));

        Free(segment.PElems - _pElems, segment.Length);
    }

    private unsafe (long Index, long Length) Alloc(long length)
    {
        if (UsedLength + length > CapacityLength)
        {
            throw new OutOfMemoryException();
        }

        int swaps = 0;

        for (; ; )
        {
            if (_indexQueue.Count == 0)
            {
                var temp = _indexQueue;
                _indexQueue = _futureQueue;
                _futureQueue = temp;
                swaps++;

                if (swaps == 2)
                {
                    throw new OutOfMemoryException();
                }
            }

            var indexCount = _indexQueue.Count;

            for(long i=0; i<indexCount; i++)
            {
                var indexEntry = _indexQueue.Dequeue();

                if (_allocatedIndexes[indexEntry.Index] == true)
                {
                    _futureQueue.Enqueue(indexEntry);
                    continue;
                }

                while (_indexQueue.TryPeek(out var nextIndexEntry) &&
                    nextIndexEntry.Index == indexEntry.Index + indexEntry.Length &&
                    _allocatedIndexes[nextIndexEntry.Index] == false)
                {
                    _indexQueue.Dequeue();
                    indexCount--;

                    indexEntry = indexEntry with { Length = indexEntry.Length + nextIndexEntry.Length };
                }

                if (indexEntry.Length >= length)
                {
                    if (indexEntry.Length > length)
                    {
                        var leftoverEntry = new IndexEntry() { Index = indexEntry.Index + length, Length = indexEntry.Length - length };
                        indexEntry = indexEntry with { Length = length };

                        _indexQueue.EnqueueHead(leftoverEntry);
                    }

                    _futureQueue.Enqueue(indexEntry);
                    _allocatedIndexes[indexEntry.Index] = true;

                    Allocations++;
                    UsedLength += length;

                    return new(indexEntry.Index, indexEntry.Length);
                }
                else
                {
                    _futureQueue.Enqueue(indexEntry);
                }
            }
        }

        throw new OutOfMemoryException();
    }

    private unsafe void Free(long index, long length)
    {
        if (_allocatedIndexes[index] == false)
        {
            throw new ArgumentException($"No rented segment found at index {index}.");
        }

        _allocatedIndexes[index] = false;

        Allocations--;
        UsedLength -= length;
    }

    public void Clear()
    {
        Allocations = 0;
        UsedLength = 0;
        _indexQueue.Clear();
        _futureQueue.Clear();
        _allocatedIndexes.Clear();
        _indexQueue.Enqueue(new IndexEntry() { Index = 0, Length = CapacityLength });
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _indexQueue.Dispose();
                _futureQueue.Dispose();
                _allocatedIndexes.Dispose();
            }

            _memoryHandle.Dispose();

            if (_privatelyOwned)
            {
                NativeMemory.Free(_pElems);
            }

            _disposed = true;
        }
    }

    ~SequentialFitSuballocator()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    readonly struct IndexEntry
    {
        private readonly long _offset;
        private readonly long _length;

        public long Index { get => _offset; init => _offset = value; }
        public long Length { get => _length; init => _length = value; }
    }
}
