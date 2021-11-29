using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;
using Suballocation.Suballocators;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Suballocation
{
    /// <summary>
    /// ISuballocator implementation backed by the shared MemoryPool.
    /// Not meant to be used other than as a poor baseline for comparison.
    /// </summary>
    /// <typeparam name="T"></typeparam>
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

        public bool TryRent(long length, out NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryPoolSuballocator<T>));
            if (length > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length), $"Segment length cannot be greater than Int32.MaxValue.");

            if (UsedLength + length > CapacityLength || _rentedArrays.Count == int.MaxValue)
            {
                segment = default;
                return false;
            }

            var arr = MemoryPool<T>.Shared.Rent((int)length).Memory;

            var handle = arr.Pin();

            _rentedArrays.Add((IntPtr)handle.Pointer, handle);

            Allocations++;
            UsedLength += length;

            segment = new NativeMemorySegment<T>((T*)handle.Pointer, length);
            return true;
        }

        public bool TryReturn(NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MemoryPoolSuballocator<T>));

            var addr = (IntPtr)segment.PElems;

            if (_rentedArrays.Remove(addr, out var handle) == false)
            {
                return false;
            }

            handle.Dispose();

            Allocations--;
            UsedLength -= segment.Length;
            return true;
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