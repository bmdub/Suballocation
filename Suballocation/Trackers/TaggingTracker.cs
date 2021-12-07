using Suballocation.Collections;

namespace Suballocation;

/// <summary>
/// Provides segment -> custom value mappings.
/// </summary>
/// <typeparam name="T">An item type to map to each segment, for later retrieval.</typeparam>
public class TaggingTracker<T>
{
    private readonly Dictionary<long, T> _dict = new();

    /// <summary>Tells the tracker to note this newly-rented or updated segment.</summary>
    /// <param name="segment">The added or updated memory segment.</param>
    /// <param name="tag">An item to associate with this segment, for later retrieval.</param>
    public unsafe void TrackAdditionOrUpdate(ISegment segment, T tag)
    {
        _dict[(long)segment.PBytes] = tag;
    }

    /// <summary>Tells the tracker to note this newly-removed segment.</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    public unsafe T RegisterRemoval(ISegment segment)
    {
        if (_dict.Remove((long)segment.PBytes, out var value) == false)
        {
            throw new KeyNotFoundException();
        }

        return value;
    }

    /// <summary>Gets the tag associated with the given segment</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    /// <returns>The tag given to the segment.</returns>
    /// <exception cref="KeyNotFoundException"></exception>
    public T this[ISegment segment]
    {
        get
        {
            if(TryGetTag(segment, out var tag) == false)
            {
                throw new KeyNotFoundException();
            }

            return tag;
        }
    }

    /// <summary>Gets the tag associated with the given segment</summary>
    /// <param name="segment">The memory segment that was removed from its buffer.</param>
    /// <param name="value">The tag given to the segment, if found.</param>
    /// <returns>True if found.</returns>
    public unsafe bool TryGetTag(ISegment segment, out T value)
    {
        return _dict.TryGetValue((long)segment.PBytes, out value!);
    }

    /// <summary>Clears all registered segments from the tracker.</summary>
    public void Clear()
    {
        _dict.Clear();
    }
}
