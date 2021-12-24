
namespace Suballocation.Suballocators;

/// <summary>
/// Provides a mechanism for retrieving information about a general suballocator.
/// </summary>
public unsafe interface ISuballocator : IDisposable
{
    /// <summary>The total size of the outstanding rented memory segments.</summary>
    public long UsedBytes { get; }

    /// <summary>The total size of backing buffer.</summary>
    public long LengthBytes { get; }

    /// <summary>The total size of the free buffer space available to the allocator (not necessarily contiguous).</summary>
    public long FreeBytes { get; }

    /// <summary>The number of outsanding rented segments.</summary>
    public long Allocations { get; }

    /// <summary>Pointer to the start of the pinned backing buffer.</summary>
    public byte* PBytes { get; }

    /// <summary>Gets the size of the allocated memory segment pointed to by the given pointer.</summary>
    /// <param name="segmentPtr">The pointer to a rented segment of memory from this allocator.</param>
    /// <returns>The byte length of the allocated segment.</returns>
    public long GetSegmentLengthBytes(byte* segmentPtr);

    /// <summary>Clones the rented memory segment, and rents the clone out to the user.</summary>
    /// <param name="sourceSegmentPtr">The pointer to a rented segment of memory to clone from.</param>
    /// <param name="destinationSegmentPtr">A pointer to a rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</param>
    /// <param name="lengthActual">The byte length of the rented segment returned, which may be >= the requested length.</param>
    /// <returns>True if successful; False if free space could not be found for this segment.</returns>
    public bool TryClone(byte* sourceSegmentPtr, out byte* destinationSegmentPtr, out long lengthActual);

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again.</summary>
    /// <param name="segmentPtr">The pointer to a rented segment of memory from this allocator.</param>
    /// <returns>The byte length of the returned segment.</returns>
    public long Return(byte* segmentPtr);
}

/// <summary>
/// Manages slices of memory segments from a fixed/large contiguous memory buffer.
/// </summary>
public unsafe interface ISuballocator<T> : ISuballocator, IEnumerable<(IntPtr SegmentPtr, long Length)> where T : unmanaged
{
    /// <summary>The total unit count of the outstanding rented memory segments.</summary>
    public long Used { get; }

    /// <summary>The total unit length of backing buffer.</summary>
    public long Length { get; }

    /// <summary>The total unit count free elements available to the allocator (not necessarily contiguous).</summary>
    public long Free { get; }

    /// <summary>Pointer to the start of the pinned backing buffer.</summary>
    public T* PElems { get; }

    /// <summary>Gets the length of the allocated memory segment pointed to by the given pointer.</summary>
    /// <param name="segmentPtr">The pointer to a rented segment of memory from this allocator.</param>
    /// <returns>The unit length of the allocated segment.</returns>
    public long GetSegmentLength(T* segmentPtr);

    /// <summary>Clones the rented memory segment, and rents the clone out to the user.</summary>
    /// <param name="sourceSegmentPtr">The pointer to a rented segment of memory to clone from.</param>
    /// <param name="destinationSegmentPtr">A pointer to a rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</param>
    /// <param name="lengthActual">The unit length of the rented segment returned, which may be >= the requested length.</param>
    /// <returns>True if successful; False if free space could not be found for this segment.</returns>
    public bool TryClone(T* sourceSegmentPtr, out T* destinationSegmentPtr, out long lengthActual);

    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <param name="segmentPtr">A pointer to a rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</param>
    /// <param name="lengthActual">The length of the rented segment returned, which may be >= the requested length.</param>
    /// <returns>True if successful; False if free space could not be found for this segment.</returns>
    public bool TryRent(long length, out T* segmentPtr, out long lengthActual);

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again.</summary>
    /// <param name="segmentPtr">The pointer to a rented segment of memory from this allocator.</param>
    /// <returns>The unit length of the returned segment.</returns>
    public long Return(T* segmentPtr);

    /// <summary>Clears all records of all outstanding rented segments, returning the allocator to an initial state. 
    /// NOTE: Behavior is undefined if any outstanding segment contents are modified after Clear() is called.</summary>
    public void Clear();
}

public static class SuballocatorExtensions
{
    /// <summary>Clones the memory segment, and rents the clone out to the user.</summary>
    /// <returns>The cloned segment.</returns>
    public static unsafe byte* Clone<T>(this ISuballocator suballocator, byte* sourceSegment)
    {
        if (suballocator.TryClone(sourceSegment, out var destinationSegmentPtr, out _) == false)
        {
            throw new OutOfMemoryException();
        }

        return destinationSegmentPtr;
    }

    /// <summary>Clones the memory segment, and rents the clone out to the user.</summary>
    /// <returns>The cloned segment.</returns>
    public static unsafe T* Clone<T>(this ISuballocator<T> suballocator, T* sourceSegment) where T : unmanaged
    {
        if (suballocator.TryClone(sourceSegment, out var destinationSegmentPtr, out _) == false)
        {
            throw new OutOfMemoryException();
        }

        return destinationSegmentPtr;
    }

    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <returns>A pointer to a rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</returns>
    public static unsafe T* Rent<T>(this ISuballocator<T> suballocator, long length = 1) where T : unmanaged
    {
        if (suballocator.TryRent(length, out var segmentPtr, out _) == false)
        {
            throw new OutOfMemoryException();
        }

        return segmentPtr;
    }
}