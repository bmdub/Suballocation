
namespace Suballocation.Collections;

/// <summary>
/// Defines the boundaries of a contiguous range.
/// </summary>
public interface IRangedEntry
{
    /// <summary>The index of the first element in the range.</summary>
    public long RangeOffset { get; }

    /// <summary>The count of elements including and following the offset.</summary>
    public long RangeLength { get; }
}
