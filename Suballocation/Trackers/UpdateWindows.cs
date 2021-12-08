
using System.Collections;

namespace Suballocation.Trackers;

/// <summary>
/// Collection that provides update 'windows', summarizing which parts of a suballocator's buffer were updated.
/// </summary>
/// <typeparam name="TSeg">A blittable element type that defines the units of a suballocation.</typeparam>
/// <typeparam name="TTag">Tag type for each the segments.</typeparam>
public class UpdateWindows<TSeg, TTag> : IEnumerable<NativeMemorySegment<TSeg, TTag>> where TSeg : unmanaged
{
    private IReadOnlyList<NativeMemorySegment<TSeg, TTag>> _windows;

    /// <summary></summary>
    /// <param name="windows">A list of segments that represent updated windows of a buffer.</param>
    public unsafe UpdateWindows(List<NativeMemorySegment<TSeg, TTag>> windows)
    {
        _windows = windows;

        if (windows.Count == 0)
        {
            return;
        }

        SpreadLength = (windows[^1].PSegment + windows[^1].Length) - windows[0].PSegment;

        foreach (var window in windows)
        {
            TotalLength += window.Length;
        }
    }

    /// <summary>The unit distance from the beginning of the lowest-addressed update window to the end of the highest-address update window.</summary>
    public long SpreadLength { get; init; }

    /// <summary>The byte length from the beginning of the lowest-addressed update window to the end of the highest-address update window.</summary>
    public long SpreadSize => SpreadLength * Unsafe.SizeOf<TSeg>();

    /// <summary>The unit sum of the lengths of all update windows.</summary>
    public long TotalLength { get; init; }

    /// <summary>The sum of the byte lengths of all update windows.</summary>
    public long TotalSize => TotalLength * Unsafe.SizeOf<TSeg>();

    /// <summary>The count of update windows.</summary>
    public long Count { get => _windows.Count; }

    /// <summary>Gets the update window at the ith position.</summary>
    public NativeMemorySegment<TSeg, TTag> this[int index] => _windows[index];

    /// <summary>Gets an enumerator over the update windows.</summary>
    public IEnumerator<NativeMemorySegment<TSeg, TTag>> GetEnumerator() => _windows.Cast<NativeMemorySegment<TSeg, TTag>>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _windows.Cast<NativeMemorySegment<TSeg, TTag>>().GetEnumerator();
}
