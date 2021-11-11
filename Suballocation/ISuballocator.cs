using System.Collections.Generic;

namespace Suballocation
{
    public interface ISuballocator : IDisposable
    {
        public long SizeUsed { get; }
        public long SizeTotal { get; }
        public long Allocations { get; }
    }

    public unsafe interface ISuballocator<T> : ISuballocator where T : unmanaged
    {
        public long LengthUsed { get; }
        public long LengthTotal { get; }
        public T* PElems { get; }
        public NativeMemorySegment<T> Rent(long length = 1);
        public void Return(NativeMemorySegment<T> segment);
        public NativeMemorySegmentResource<T> RentResource(long length = 1);
        public void ReturnResource(NativeMemorySegmentResource<T> segment);
        public void Clear();
    }
}
