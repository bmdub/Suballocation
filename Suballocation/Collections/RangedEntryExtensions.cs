
namespace Suballocation.Collections;

/// <summary>
/// Extension methods for IRangedEntry.
/// </summary>
public static class RangedEntryExtensions
{
    /// <summary>Returns the inclusive end index of the range.</summary>
    public static long RangeEndOffset(this IRangedEntry rangedEntry)
    {
        return rangedEntry.RangeOffset + rangedEntry.RangeLength - 1;
    }
}