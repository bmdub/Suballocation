
namespace Suballocation;

/// <summary>
/// Provides the ability to track rented memory segments from a suballocator. Specifically, the portions of the buffer that were allocated or updated.
/// Also can combine update windows automatically based on a configurable distance threshold.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units allocated.</typeparam>
public class UpdateWindowTracker1<T> where T : unmanaged
{
    private readonly Stack<NativeMemorySegment<T>> _windowsPrev = new();
    private readonly Stack<NativeMemorySegment<T>> _windowsNext = new();
    private readonly double _minimumFillPercentage;

    /// <summary></summary>
    /// <param name="minimumFillPercentage">A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined.</param>
    public UpdateWindowTracker1(double minimumFillPercentage)
    {
        _minimumFillPercentage = minimumFillPercentage;
    }

    /// <summary>A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined.</summary>
    public double MinimumFillPercentage => _minimumFillPercentage;

    /// <summary>Tells the tracker to note this newly-rented or updated segment.</summary>
    /// <param name="segment"></param>
    public unsafe void RegisterUpdate(ISegment<T> segment)
    {
        // Given where we left off before, navigate the two stacks, which represent the update windows on either side of the previous update.
        // We will shift update windows between the two stacks while we find a place for this new update window.
        // We'll also look for opportunities to combine windows.
        bool havePrevWindow = _windowsPrev.TryPeek(out var prevWindow);
        bool haveNextWindow = _windowsNext.TryPeek(out var nextWindow);

        while (haveNextWindow && nextWindow.PElems < segment.PElems)
        {
            prevWindow = _windowsNext.Pop();
            _windowsPrev.Push(prevWindow);
            havePrevWindow = true;

            haveNextWindow = _windowsNext.TryPeek(out nextWindow);
        }

        while (havePrevWindow && prevWindow.PElems > segment.PElems)
        {
            nextWindow = _windowsPrev.Pop();
            _windowsNext.Push(nextWindow);
            haveNextWindow = true;

            havePrevWindow = _windowsPrev.TryPeek(out prevWindow);
        }

        if (haveNextWindow && CanCombine(segment.PElems, segment.LengthBytes, nextWindow.PElems, nextWindow.LengthBytes))
        {
            _windowsNext.Pop();
            nextWindow = nextWindow with { PElems = segment.PElems, Length = ((long)nextWindow.PElems + nextWindow.LengthBytes - (long)segment.PElems) / Unsafe.SizeOf<T>() };

            while (_windowsNext.TryPeek(out var seg) && CanCombine(nextWindow.PElems, nextWindow.LengthBytes, seg.PElems, seg.LengthBytes))
            {
                _windowsNext.Pop();
                nextWindow = nextWindow with { PElems = nextWindow.PElems, Length = ((long)seg.PElems + seg.LengthBytes - (long)nextWindow.PElems) / Unsafe.SizeOf<T>() };
            }

            _windowsNext.Push(nextWindow);

            return;
        }

        if (havePrevWindow && CanCombine(prevWindow.PElems, prevWindow.LengthBytes, segment.PElems, segment.LengthBytes))
        {
            _windowsPrev.Pop();
            prevWindow = prevWindow with { PElems = prevWindow.PElems, Length = ((long)segment.PElems + segment.LengthBytes - (long)prevWindow.PElems) / Unsafe.SizeOf<T>() };

            while (_windowsPrev.TryPeek(out var seg) && CanCombine(seg.PElems, seg.LengthBytes, prevWindow.PElems, prevWindow.LengthBytes))
            {
                _windowsPrev.Pop();
                prevWindow = prevWindow with { PElems = seg.PElems, Length = ((long)prevWindow.PElems + prevWindow.LengthBytes - (long)seg.PElems) / Unsafe.SizeOf<T>() };
            }

            _windowsPrev.Push(prevWindow);
        }

        _windowsPrev.Push(new NativeMemorySegment<T>() { PElems = segment.PElems, Length = segment.Length });
    }

    /// <summary>Used to determine if we can combine two update windows.</summary>
    private unsafe bool CanCombine(T* pElemsPrev, long sizePrev, T* pElemsNext, long sizeNext)
    {
        return (sizeNext + sizePrev) / (double)((long)pElemsNext + sizeNext - (long)pElemsPrev) >= _minimumFillPercentage;
    }

    /// <summary>Builds the final set of optimized/combined update windows based on the registered segment updates.</summary>
    /// <returns></returns>
    public unsafe UpdateWindows<T> BuildUpdateWindows()
    {
        // Do one final pass to try and combine the update windows we have.
        while (_windowsPrev.TryPop(out var window))
        {
            _windowsNext.Push(window);
        }

        List<NativeMemorySegment<T>> windows = new List<NativeMemorySegment<T>>(_windowsNext.Count);

        foreach (var window in _windowsNext)
        {
            if (windows.Count > 0 && CanCombine(windows[^1].PElems, windows[^1].LengthBytes, window.PElems, window.LengthBytes))
            {
                windows[^1] = window with { PElems = windows[^1].PElems, Length = ((long)window.PElems + window.LengthBytes - (long)windows[^1].PElems) / Unsafe.SizeOf<T>() };
            }
            else
            {
                windows.Add(window);
            }
        }

        return new UpdateWindows<T>(windows);
    }

    /// <summary>Clears the tracker of all state, so it can be reused.</summary>
    public void Clear()
    {
        _windowsPrev.Clear();
        _windowsNext.Clear();
    }
}

/// <summary>
/// Provides the ability to track rented memory segments from a suballocator. Specifically, the portions of the buffer that were allocated or updated.
/// Also can combine update windows automatically based on a configurable distance threshold.
/// </summary>
/// <typeparam name="T">A blittable element type that defines the units allocated.</typeparam>
public class UpdateWindowTracker2<T> where T : unmanaged
{
    private static readonly Comparer<NativeMemorySegment<T>> _segmentComparer;
    private readonly List<NativeMemorySegment<T>> _olderWindows = new();
    private readonly double _minimumFillPercentage;
    private NativeMemorySegment<T> _currentWindow;

    static unsafe UpdateWindowTracker2()
    {
        _segmentComparer = Comparer<NativeMemorySegment<T>>.Create((a, b) => ((IntPtr)a.PElems).CompareTo((IntPtr)b.PElems));
    }

    /// <summary></summary>
    /// <param name="minimumFillPercentage">A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined.</param>
    public UpdateWindowTracker2(double minimumFillPercentage)
    {
        _minimumFillPercentage = minimumFillPercentage;
    }

    /// <summary>A threshold between 0 and 1. If any two update windows have a "used" to "combined length" ratio above this, then they will be combined.</summary>
    public double MinimumFillPercentage => _minimumFillPercentage;

    /// <summary>Tells the tracker to note this newly-rented or updated segment.</summary>
    /// <param name="segment"></param>
    public unsafe void RegisterUpdate(ISegment<T> segment)
    {
        // Given the previous segment update, see if this new segment can be combined with the previous one.
        // Otherwise, record it for later optimization.
        if (_currentWindow.Length == 0)
        {
            _currentWindow = new NativeMemorySegment<T>() { PElems = segment.PElems, Length = segment.Length };
            return;
        }

        if (segment.PElems < _currentWindow.PElems)
        {
            if (CanCombine(segment.PElems, segment.LengthBytes, _currentWindow.PElems, _currentWindow.LengthBytes))
            {
                _currentWindow = _currentWindow with { PElems = segment.PElems, Length = (_currentWindow.LengthBytes + segment.LengthBytes) / Unsafe.SizeOf<T>() };
                return;
            }
        }
        else
        {
            if (CanCombine(_currentWindow.PElems, _currentWindow.LengthBytes, segment.PElems, segment.LengthBytes))
            {
                _currentWindow = _currentWindow with { PElems = _currentWindow.PElems, Length = (_currentWindow.LengthBytes + segment.LengthBytes) / Unsafe.SizeOf<T>() };
                return;
            }
        }

        _olderWindows.Add(_currentWindow);
        _currentWindow = new NativeMemorySegment<T>() { PElems = segment.PElems, Length = segment.Length };
    }

    /// <summary>Used to determine if we can combine two update windows.</summary>
    private unsafe bool CanCombine(T* pElemsPrev, long sizePrev, T* pElemsNext, long sizeNext)
    {
        return (sizeNext + sizePrev) / (double)((long)pElemsNext + sizeNext - (long)pElemsPrev) >= _minimumFillPercentage;
    }

    /// <summary>Builds the final set of optimized/combined update windows based on the registered segment updates.</summary>
    /// <returns></returns>
    public unsafe UpdateWindows<T> BuildUpdateWindows()
    {
        // Do one final pass to try and combine the update windows we have.
        if (_currentWindow.Length > 0)
        {
            _olderWindows.Add(_currentWindow);
            _currentWindow = default;
        }

        _olderWindows.Sort(_segmentComparer);

        List<NativeMemorySegment<T>> windows = new List<NativeMemorySegment<T>>(_olderWindows.Count);

        foreach (var window in _olderWindows)
        {
            if (windows.Count > 0 && CanCombine(windows[^1].PElems, windows[^1].LengthBytes, window.PElems, window.LengthBytes))
            {
                windows[^1] = window with { PElems = windows[^1].PElems, Length = ((long)window.PElems + window.LengthBytes - (long)windows[^1].PElems) / Unsafe.SizeOf<T>() };
            }
            else
            {
                windows.Add(window);
            }
        }

        return new UpdateWindows<T>(windows);
    }

    /// <summary>Clears the tracker of all state, so it can be reused.</summary>
    public void Clear()
    {
        _olderWindows.Clear();
        _currentWindow = default;
    }
}
