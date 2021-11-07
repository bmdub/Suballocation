using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation
{
    public unsafe sealed class StackSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        private readonly T* _pElems;
        private readonly MemoryHandle _memoryHandle;
        private readonly bool _privatelyOwned;
        private readonly Stack<IndexEntry> _indexes = new();
        private bool _disposed;

        public StackSuballocator(long length, bool zeroed)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

            LengthTotal = length;

            if (zeroed)
            {
                _pElems = (T*)NativeMemory.AllocZeroed((nuint)length, (nuint)Unsafe.SizeOf<T>());
            }
            else
            {
                _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
            }

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

        public long LengthUsed { get; private set; }

        public long LengthTotal { get; init; }

        public long Allocations { get; private set; }

        public UnmanagedMemorySegmentResource<T> RentResource(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new UnmanagedMemorySegmentResource<T>(this, _pElems + rawSegment.Index, rawSegment.Length);
        }

        public void ReturnResource(UnmanagedMemorySegmentResource<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        public UnmanagedMemorySegment<T> Rent(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new UnmanagedMemorySegment<T>(_pElems + rawSegment.Index, rawSegment.Length);
        }

        public void Return(UnmanagedMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        private unsafe (long Index, long Length) Alloc(long length)
        {
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            if(LengthUsed + length >= LengthTotal)
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
            if (_indexes.TryPop(out var topEntry) == false)
            {
                throw new ArgumentException($"No rented segments found.");
            }

            if (topEntry.Index != index)
            {
                throw new ArgumentException($"Returned segment does not have the expected index ({}).");
            }

            if (topEntry.Length != length)
            {
                throw new ArgumentException($"Returned segment does not have expected length ({foundLength:N0}).");
            }

            bool success = _indexes.TryPop(out _);
            Debug.Assert(success, "Unable to pop from index stack.");

            Allocations--;
            LengthUsed -= length;
        }

#if TRACE_OUTPUT
        public void PrintBlocks()
        {
            var list = _prevIndexes
                .Concat(_nextIndexes)
                .OrderBy(entry => entry.Offset)
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                if (i < list.Count)
                {
                    char c = _occupiedIndexes.TryGetValue(list[i].Offset, out _) ? 'X' : '_';
                    for (long j = 0; j < list[i].Length / BlockLength; j++)
                        Console.Write(c);
                }
            }

            Console.WriteLine();
        }
#endif

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null

                _memoryHandle.Dispose(); //todo: if default, what will happen?

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
