namespace Suballocation;

/// <summary>
/// Collection that contains a list of update windows, indicating which parts of a suballocator's buffer were updated.
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IUpdateWindows : IEnumerable<ISegment>
{
    /// <summary>The byte length from the beginning of the lowest-addressed update window to the end of the highest-address update window.</summary>
    long SpreadSize { get; }

    /// <summary>The sum of the byte lengths of all update windows.</summary>
    long TotalSize { get; }

    /// <summary>The count of update windows.</summary>
    long Count { get; }

    /// <summary>Gets the update window at the ith position.</summary>
    ISegment this[int index] { get; }
}
