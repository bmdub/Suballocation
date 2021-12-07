using System;
using System.Buffers;
using Suballocation.Suballocators;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Suballocation
{
    /// <summary>
    /// ISuballocator implementation backed by the shared ArrayPool.
    /// Not meant to be used other than as a poor baseline for comparison.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public unsafe sealed class ArrayPoolSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        private readonly uint _id;
        private Dictionary<IntPtr, GCHandle>? _rentedArrays;
        private bool _disposed;

        public ArrayPoolSuballocator(long length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");

            _rentedArrays = new((int)Math.Min(int.MaxValue, length));

            // Note: We are artificially limiting ArrayPool here.
            Length = length;

            _id = SuballocatorTable<T>.Register(this);
        }

        public long UsedBytes => Used * Unsafe.SizeOf<T>();

        public long LengthBytes => Length * Unsafe.SizeOf<T>();

        public long FreeBytes { get => LengthBytes - UsedBytes; }

        public long Allocations { get; private set; }

        public long Used { get; private set; }

        public long Length { get; init; }

        public long Free { get => Length - Used; }

        public T* PElems => throw new NotImplementedException();

        public byte* PBytes => throw new NotImplementedException();

        public bool TryRent(long length, out NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArrayPoolSuballocator<T>));
            if (length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length cannot be greater than Int32.MaxValue.");

            if (Used + length > Length || _rentedArrays.Count == int.MaxValue)
            {
                segment = default;
                return false;
            }

            var arr = ArrayPool<T>.Shared.Rent((int)length);

            var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);

            _rentedArrays.Add(handle.AddrOfPinnedObject(), handle);

            Allocations++;
            Used += length;

            segment = new NativeMemorySegment<T>(_id, (T*)handle.AddrOfPinnedObject(), length);
            return true;
        }

        public bool TryReturn(NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArrayPoolSuballocator<T>));

            var addr = (IntPtr)segment.PElems;

            if (_rentedArrays.Remove(addr, out var handle) == false)
            {
                return false;
            }

            var arr = handle.Target as T[];

            if(arr != null)
            {
                ArrayPool<T>.Shared.Return(arr);
            }

            handle.Free();

            Allocations--;
            Used -= segment.Length;
            return true;
        }

        public void Clear()
        {
            Used = 0;
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

        public IEnumerator<NativeMemorySegment<T>> GetEnumerator() => throw new NotImplementedException();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            _rentedArrays = null;

            _disposed = true;
        }
    }
}

#pragma warning restore CS8602 // Dereference of a possibly null reference.