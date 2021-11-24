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
    public unsafe sealed class ArrayPoolSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        private Dictionary<IntPtr, GCHandle>? _rentedArrays;
        private bool _disposed;

        public ArrayPoolSuballocator(long length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

            _rentedArrays = new((int)Math.Min(int.MaxValue, length));

            // Note: We are artificially limiting ArrayPool here.
            CapacityLength = length;
        }

        public long LengthBytesUsed => UsedLength * Unsafe.SizeOf<T>();

        public long LengthBytesTotal => CapacityLength * Unsafe.SizeOf<T>();

        public long Allocations { get; private set; }

        public long UsedLength { get; private set; }

        public long CapacityLength { get; init; }

        public T* PElems => throw new NotImplementedException();

        public NativeMemorySegmentResource<T> RentResource(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArrayPoolSuballocator<T>));
            if (length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length cannot be greater than Int32.MaxValue.");

            var rawSegment = Alloc(length);

            return new NativeMemorySegmentResource<T>(this, (T*)rawSegment.Index, rawSegment.Length);
        }

        public void ReturnResource(NativeMemorySegmentResource<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArrayPoolSuballocator<T>));

            Free((long)segment.PElems, segment.Length);
        }

        public NativeMemorySegment<T> Rent(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArrayPoolSuballocator<T>));
            if (length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length cannot be greater than Int32.MaxValue.");

            var rawSegment = Alloc(length);

            return new NativeMemorySegment<T>((T*)rawSegment.Index, rawSegment.Length);
        }

        public void Return(NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArrayPoolSuballocator<T>));

            Free((long)segment.PElems, segment.Length);
        }

        private unsafe (long Index, long Length) Alloc(long length)
        {
            if (UsedLength + length > CapacityLength || _rentedArrays.Count == int.MaxValue)
            {
                throw new OutOfMemoryException();
            }

            var arr = ArrayPool<T>.Shared.Rent((int)length);

            var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);

            _rentedArrays.Add(handle.AddrOfPinnedObject(), handle);

            Allocations++;
            UsedLength += length;

            return new((long)handle.AddrOfPinnedObject(), length);
        }

        private unsafe void Free(long index, long length)
        {
            var addr = (IntPtr)index;

            if (_rentedArrays.Remove(addr, out var handle) == false)
            {
                throw new ArgumentException($"Rented segment not found.");
            }

            var arr = handle.Target as T[];

            if(arr != null)
            {
                ArrayPool<T>.Shared.Return(arr);
            }

            handle.Free();

            Allocations--;
            UsedLength -= length;
        }

        public void Clear()
        {
            UsedLength = 0;
            Allocations = 0;

            foreach(var handle in _rentedArrays.Values)
            {
                var arr = handle.Target as T[];

                if (arr != null)
                {
                    ArrayPool<T>.Shared.Return(arr);
                }

                handle.Free();
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