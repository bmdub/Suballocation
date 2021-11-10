using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation
{
    public unsafe sealed class StackSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        private readonly T* _pElems;
        private readonly MemoryHandle _memoryHandle;
        private readonly bool _privatelyOwned;
        private readonly NativeStack<IndexEntry> _indexes = new();
        private bool _disposed;

        public StackSuballocator(long length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

            LengthTotal = length;

            _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
            _privatelyOwned = true;

            _indexes.Push(new IndexEntry() { Index = 0, Length = length });
        }

        public StackSuballocator(T* pData, long length)
        {
            if (pData == null) throw new ArgumentNullException(nameof(pData));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

            LengthTotal = length;

            _pElems = pData;

            _indexes.Push(new IndexEntry() { Index = 0, Length = length });
        }

        public StackSuballocator(Memory<T> data)
        {
            LengthTotal = data.Length;

            _memoryHandle = data.Pin();
            _pElems = (T*)_memoryHandle.Pointer;

            _indexes.Push(new IndexEntry() { Index = 0, Length = data.Length });
        }

        public long SizeUsed => LengthUsed * Unsafe.SizeOf<T>();

        public long SizeTotal => LengthTotal * Unsafe.SizeOf<T>();

        public long Allocations { get; private set; }

        public long LengthUsed { get; private set; }

        public long LengthTotal { get; init; }

        public T* PElems => _pElems;

        public UnmanagedMemorySegmentResource<T> RentResource(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new UnmanagedMemorySegmentResource<T>(this, _pElems + rawSegment.Index, rawSegment.Length);
        }

        public void ReturnResource(UnmanagedMemorySegmentResource<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        public UnmanagedMemorySegment<T> Rent(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new UnmanagedMemorySegment<T>(_pElems + rawSegment.Index, rawSegment.Length);
        }

        public void Return(UnmanagedMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        private unsafe (long Index, long Length) Alloc(long length)
        {
            if (LengthUsed + length > LengthTotal)
            {
                throw new OutOfMemoryException();
            }

            var indexEntry = new IndexEntry() { Index = LengthUsed, Length = length };

            _indexes.Push(indexEntry);

            Allocations++;
            LengthUsed += length;

            return new(indexEntry.Index, indexEntry.Length);
        }

        private unsafe void Free(long index, long length)
        {
            if (_indexes.TryPeek(out var topEntry) == false)
            {
                throw new ArgumentException($"No rented segments found.");
            }

            if (topEntry.Index != index)
            {
                throw new ArgumentException($"Returned segment does not have the expected index ({topEntry.Index}).");
            }

            if (topEntry.Length != length)
            {
                throw new ArgumentException($"Returned segment does not have expected length ({topEntry.Length:N0}).");
            }

            bool success = _indexes.TryPop(out _);
            Debug.Assert(success, "Unable to pop from index stack.");

            Allocations--;
            LengthUsed -= length;
        }

        public void Clear()
        {
            Allocations = 0;
            LengthUsed = 0;
            _indexes.Clear();
            _indexes.Push(new IndexEntry() { Index = 0, Length = LengthTotal });
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _indexes.Dispose();
                }

                _memoryHandle.Dispose(); 

                if (_privatelyOwned)
                {
                    NativeMemory.Free(_pElems);
                }

                _disposed = true;
            }
        }

        ~StackSuballocator()
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
}
