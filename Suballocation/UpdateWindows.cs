
using System.Collections;

namespace Suballocation;

/// <summary>
/// Collection that contains a list of update windows, indicating which parts of a suballocator's buffer were updated.
/// </summary>
/// <typeparam name="T"></typeparam>
public class UpdateWindows<T> : IEnumerable<ISegment<T>>, IUpdateWindows where T : unmanaged
{
    private IReadOnlyList<NativeMemorySegment<T>> _windows;

    /// <summary></summary>
    /// <param name="windows">A list of segments that represent updated windows of a buffer.</param>
    public unsafe UpdateWindows(List<NativeMemorySegment<T>> windows)
    {
        _windows = windows;

        if (windows.Count == 0)
        {
            return;
        }

        SpreadLength = (windows[^1].PElems + windows[^1].Length) - windows[0].PElems;

        foreach (var window in windows)
        {
            TotalLength += window.Length;
        }
    }

    /// <summary>The unit distance from the beginning of the lowest-addressed update window to the end of the highest-address update window.</summary>
    public long SpreadLength { get; init; }

    public long SpreadSize => SpreadLength * Unsafe.SizeOf<T>();

    /// <summary>The unit sum of the lengths of all update windows.</summary>
    public long TotalLength { get; init; }

    public long TotalSize => TotalLength * Unsafe.SizeOf<T>();

    public long Count { get => _windows.Count; }

    /// <summary>Gets the update window at the ith position.</summary>
    public ISegment<T> this[int index] => _windows[index];

    ISegment IUpdateWindows.this[int index] => _windows[index];

    /// <summary>Gets an enumerator over the update windows.</summary>
    public IEnumerator<ISegment<T>> GetEnumerator() => _windows.Cast<ISegment<T>>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _windows.Cast<ISegment<T>>().GetEnumerator();

    IEnumerator<ISegment> IEnumerable<ISegment>.GetEnumerator() => _windows.Cast<ISegment<T>>().GetEnumerator();
}
