
namespace Suballocation.Suballocators;

/// <summary>
/// Provides a mechanism for retrieving information about a general suballocator.
/// </summary>
public unsafe interface ISuballocator : IDisposable
{
    /// <summary>The total size of the outstanding rented memory segments.</summary>
    public long UsedBytes { get; }

    /// <summary>The total size of backing buffer.</summary>
    public long CapacityBytes { get; }

    /// <summary>The total size of the free buffer space available to the allocator (not necessarily contiguous).</summary>
    public long FreeBytes { get; }

    /// <summary>The number of outsanding rented segments.</summary>
    public long Allocations { get; }

    /// <summary>Pointer to the start of the pinned backing buffer.</summary>
    public byte* PBytes { get; }
}

/// <summary>
/// Manages slices of memory segments from a fixed/large continguous memory buffer.
/// </summary>
public unsafe interface ISuballocator<T> : ISuballocator where T : unmanaged
{
    /// <summary>The total unit count of the outstanding rented memory segments.</summary>
    public long UsedLength { get; }

    /// <summary>The total unit length of backing buffer.</summary>
    public long CapacityLength { get; }

    /// <summary>The total unit count free elements available to the allocator (not necessarily contiguous).</summary>
    public long FreeLength { get; }

    /// <summary>Pointer to the start of the pinned backing buffer.</summary>
    public T* PElems { get; }

    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <param name="segment">A rented segment that must be returned to the allocator in the future to free the memory for subsequent usage.</param>
    /// <returns>True if successful; False if free space could not be found for this segment.</returns>
    public bool TryRent(long length, out NativeMemorySegment<T> segment);

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again.</summary>
    /// <param name="segment">A previously rented segment of memory from this allocator.</param>
    /// <returns>True if successful; False if the suballocator presently has no free segment (by the given definition) to free.</returns>
    public bool TryReturn(NativeMemorySegment<T> segment);

    /// <summary>Clears all records of all outstanding rented segments, returning the allocator to an initial state. 
    /// NOTE: This assumes that prior rented segments are no longer in use, else behavior is undefined.</summary>
    public void Clear();
}

public static class ISuballocatorExtensions
{
    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <returns>A rented segment that must be returned to the allocator in the future to free the memory for subsequent usage.</returns>
    public static NativeMemorySegment<T> Rent<T>(this ISuballocator<T> suballocator, long length = 1) where T : unmanaged
    {
        if (suballocator.TryRent(length, out var segment) == false)
        {
            throw new OutOfMemoryException();
        }

        return segment;
    }

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again.</summary>
    /// <param name="segment">A previously rented segment of memory from this allocator.</param>
    public static void Return<T>(this ISuballocator<T> suballocator, NativeMemorySegment<T> segment) where T : unmanaged
    {
        if (suballocator.TryReturn(segment) == false)
        {
            throw new ArgumentException($"Segment not found.");
        }
    }
}