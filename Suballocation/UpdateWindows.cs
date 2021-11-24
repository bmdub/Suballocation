
namespace Suballocation;

public class UpdateWindows<T> where T : unmanaged
{
    public unsafe UpdateWindows(List<NativeMemorySegment<T>> windows, long totalWindowLengthUsed)
    {
        Windows = windows;

        if (windows.Count == 0)
        {
            return;
        }

        SpreadLength = (windows[^1].PElems + windows[^1].Length) - windows[0].PElems;

        foreach (var window in windows)
        {
            TotalLength += window.Length;
        }

        TotalFillPercentage = totalWindowLengthUsed / (double)TotalLength;
        SpreadFillPercentage = totalWindowLengthUsed / (double)SpreadLength;
    }

    public IReadOnlyList<NativeMemorySegment<T>> Windows { get; init; }

    public long SpreadLength { get; init; }
    public long SpreadSize => SpreadLength * Unsafe.SizeOf<T>();
    public double SpreadFillPercentage { get; init; }
    public long TotalLength { get; init; }
    public long TotalSize => TotalLength * Unsafe.SizeOf<T>();
    public double TotalFillPercentage { get; init; }
}
