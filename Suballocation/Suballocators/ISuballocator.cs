
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
}

/// <summary>
/// Manages slices of memory segments from a fixed/large continguous memory buffer.
/// </summary>
public unsafe interface ISuballocator<TSeg> : ISuballocator<TSeg, EmptyStruct> where TSeg : unmanaged
{ }

/// <summary>
/// Manages slices of memory segments from a fixed/large continguous memory buffer.
/// </summary>
public unsafe interface ISuballocator<TSeg, TTag> : ISuballocator, IEnumerable<NativeMemorySegment<TSeg, TTag>> where TSeg : unmanaged
{
    /// <summary>The total unit count of the outstanding rented memory segments.</summary>
    public long Used { get; }

    /// <summary>The total unit length of backing buffer.</summary>
    public long Length { get; }

    /// <summary>The total unit count free elements available to the allocator (not necessarily contiguous).</summary>
    public long Free { get; }

    /// <summary>Pointer to the start of the pinned backing buffer.</summary>
    public TSeg* PElems { get; }

    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <param name="segment">A rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</param>
    /// <param name="tag">Optional tag item to be associated with each segment (but is not used within the segment).</param>
    /// <returns>True if successful; False if free space could not be found for this segment.</returns>
    public bool TryRent(long length, out NativeMemorySegment<TSeg, TTag> segment, TTag tag = default!);

    /// <summary>Returns the tag associated with the given rented segment.</summary>
    /// <param name="segmentPtr">The pointer to a rented segment of memory from this allocator.</param>
    public TTag GetTag(TSeg* segmentPtr);

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again. Could be called in place of Dispose() on a segment.</summary>
    /// <param name="segment">A previously rented segment of memory from this allocator.</param>
    public void Return(NativeMemorySegment<TSeg, TTag> segment);

    /// <summary>Disposes of the given rented memory segment, and makes the memory available for rent once again.</summary>
    /// <param name="segmentPtr">The pointer to a rented segment of memory from this allocator.</param>
    public void Return(TSeg* segmentPtr);

    /// <summary>Clears all records of all outstanding rented segments, returning the allocator to an initial state. 
    /// NOTE: Behavior is undefined if any outstanding segment contents are modified after Clear() is called.</summary>
    public void Clear();
}

public static class ISuballocatorExtensions
{
    /// <summary>Returns a free segment of memory of the desired length.</summary>
    /// <param name="length">The unit length of the segment requested.</param>
    /// <returns>A rented segment that must be returned to the allocator in order to free the memory for subsequent usage.</returns>
    public static NativeMemorySegment<TSeg, TTag> Rent<TSeg, TTag>(this ISuballocator<TSeg, TTag> suballocator, long length = 1, TTag tag = default!) where TSeg : unmanaged
    {
        if (suballocator.TryRent(length, out var segment, tag) == false)
        {
            throw new OutOfMemoryException();
        }

        return segment;
    }
}