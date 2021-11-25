using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Suballocation
{
    public unsafe sealed class MemoryPoolSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        private Dictionary<IntPtr, MemoryHandle>? _rentedArrays;
        private bool _disposed;

        public MemoryPoolSuballocator(long length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

            _rentedArrays = new((int)Math.Min(int.MaxValue, length));

            // Note: We are artificially limiting ArrayPool here.
            CapacityLength = length;
        }

        public long UsedBytes => UsedLength * Unsafe.SizeOf<T>();

        public long CapacityBytes => CapacityLength * Unsafe.SizeOf<T>();

        public long FreeBytes { get => CapacityBytes - UsedBytes; }

        public long Allocations { get; private set; }

        public long UsedLength { get; private set; }

        public long CapacityLength { get; init; }

        public long FreeLength { get => CapacityLength - UsedLength; }

        public T* PElems => throw new NotImplementedException();

        public byte* PBytes => throw new NotImplementedException();

        public NativeMemorySegmentResource<T> RentResource(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryPoolSuballocator<T>));
            if (length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length cannot be greater than Int32.MaxValue.");

            var rawSegment = Alloc(length);

            return new NativeMemorySegmentResource<T>(this, (T*)rawSegment.Index, rawSegment.Length);
        }

        public void ReturnResource(NativeMemorySegmentResource<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryPoolSuballocator<T>));

            Free((long)segment.PElems, segment.Length);
        }

        public NativeMemorySegment<T> Rent(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryPoolSuballocator<T>));
            if (length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length cannot be greater than Int32.MaxValue.");

            var rawSegment = Alloc(length);

            return new NativeMemorySegment<T>((T*)rawSegment.Index, rawSegment.Length);
        }

        public void Return(NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryPoolSuballocator<T>));

            Free((long)segment.PElems, segment.Length);
        }

        private unsafe (long Index, long Length) Alloc(long length)
        {
            if (UsedLength + length > CapacityLength || _rentedArrays.Count == int.MaxValue)
            {
                throw new OutOfMemoryException();
            }

            var arr = MemoryPool<T>.Shared.Rent((int)length).Memory;

            var handle = arr.Pin();

            _rentedArrays.Add((IntPtr)handle.Pointer, handle);

            Allocations++;
            UsedLength += length;

            return new((long)handle.Pointer, length);
        }

        private unsafe void Free(long index, long length)
        {
            var addr = (IntPtr)index;

            if (_rentedArrays.Remove(addr, out var handle) == false)
            {
                throw new ArgumentException($"Rented segment not found.");
            }

            handle.Dispose();

            Allocations--;
            UsedLength -= length;
        }

        public void Clear()
        {
            UsedLength = 0;
            Allocations = 0;

            foreach (var handle in _rentedArrays.Values)
            {
                handle.Dispose();
            }
            _rentedArrays.Clear();
        }

        public void Dispose()
        {
            _rentedArrays = null;

            _disposed = true;
        }
    }
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.