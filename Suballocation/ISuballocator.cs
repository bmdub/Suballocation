using System.Collections.Generic;

namespace Suballocation
{
    public interface ISuballocator
    {
        public long SizeUsed { get; }
        public long SizeTotal { get; }
        public long Allocations { get; }
    }

    public interface ISuballocator<T> : ISuballocator where T : unmanaged
    {
        public long LengthUsed { get; }
        public long LengthTotal { get; }
        public UnmanagedMemorySegment<T> Rent(long length = 1);
        public void Return(UnmanagedMemorySegment<T> segment);
        public UnmanagedMemorySegmentResource<T> RentResource(long length = 1);
        public void ReturnResource(UnmanagedMemorySegmentResource<T> segment);
    }
}
