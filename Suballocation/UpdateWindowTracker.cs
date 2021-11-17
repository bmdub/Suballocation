
using System.Runtime.CompilerServices;

namespace Suballocation
{
    public class UpdateWindowTracker1<T> where T : unmanaged
    {
        private readonly Stack<NativeMemorySegment<T>> _windowsPrev = new();
        private readonly Stack<NativeMemorySegment<T>> _windowsNext = new();
        private readonly double _minimumFillPercentage;
        private long _totalWindowLengthUsed;

        public UpdateWindowTracker1(double minimumFillPercentage)
        {
            _minimumFillPercentage = minimumFillPercentage;
        }

        public double MinimumFillPercentage => _minimumFillPercentage;

        public unsafe void Register(ISegment<T> segment)
        {
            _totalWindowLengthUsed += segment.Length;

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

        private unsafe bool CanCombine(T* pElemsPrev, long sizePrev, T* pElemsNext, long sizeNext)
        {
            //var diff = (pElemsNext + lengthNext - pElemsPrev);
            //var value = (lengthNext + lengthPrev) / (double)((long)pElemsNext + lengthNext - (long)pElemsPrev);
            //var thresh = (_minimumFillPercentage / (_windowsPrev.Count + _windowsNext.Count));
            return (sizeNext + sizePrev) / (double)((long)pElemsNext + sizeNext - (long)pElemsPrev) >= (_minimumFillPercentage);// / (_windowsPrev.Count + _windowsNext.Count));
        }

        public unsafe UpdateWindows<T> BuildUpdateWindows()
        {
            while(_windowsPrev.TryPop(out var window))
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

            return new UpdateWindows<T>(windows, _totalWindowLengthUsed);
        }

        public void Clear()
        {
            _windowsPrev.Clear();
            _windowsNext.Clear();
        }
    }

    public class UpdateWindowTracker2<T> where T : unmanaged
    {
        private static readonly Comparer<NativeMemorySegment<T>> _segmentComparer;
        private readonly List<NativeMemorySegment<T>> _olderWindows = new();
        private readonly double _minimumFillPercentage;
        private NativeMemorySegment<T> _currentWindow;
        private long _totalWindowLengthUsed;

        static unsafe UpdateWindowTracker2()
        {
            _segmentComparer = Comparer<NativeMemorySegment<T>>.Create((a, b) => ((IntPtr)a.PElems).CompareTo((IntPtr)b.PElems));
        }

        public UpdateWindowTracker2(double minimumFillPercentage)
        {
            _minimumFillPercentage = minimumFillPercentage;
        }

        public double MinimumFillPercentage => _minimumFillPercentage;

        public unsafe void Register(ISegment<T> segment)
        {
            _totalWindowLengthUsed += segment.Length;

            if (_currentWindow.Length == 0)
            {
                _currentWindow = new NativeMemorySegment<T>() { PElems = segment.PElems, Length = segment.Length };
                return;
            }

            if (segment.PElems < _currentWindow.PElems)
            {
                if (CanCombine(segment.PElems, segment.Length, _currentWindow.PElems, _currentWindow.Length))
                {
                    _currentWindow = _currentWindow with { PElems = segment.PElems, Length = _currentWindow.Length + segment.Length };
                    return;
                }
            }
            else
            {
                if (CanCombine(_currentWindow.PElems, _currentWindow.Length, segment.PElems, segment.Length))
                {
                    _currentWindow = _currentWindow with { PElems = _currentWindow.PElems, Length = _currentWindow.Length + segment.Length };
                    return;
                }
            }

            _olderWindows.Add(_currentWindow);
            _currentWindow = new NativeMemorySegment<T>() { PElems = segment.PElems, Length = segment.Length };
        }

        private unsafe bool CanCombine(T* pElemsPrev, long lengthPrev, T* pElemsNext, long lengthNext)
        {
            return (lengthNext + lengthPrev) / (pElemsNext + lengthNext - pElemsPrev) > _minimumFillPercentage;
        }

        public unsafe UpdateWindows<T> BuildUpdateWindows()
        {
            if (_currentWindow.Length > 0)
            {
                _olderWindows.Add(_currentWindow);
                _currentWindow = default;
            }

            _olderWindows.Sort(_segmentComparer);

            List<NativeMemorySegment<T>> windows = new List<NativeMemorySegment<T>>(_olderWindows.Count);

            foreach (var window in _olderWindows)
            {
                if (windows.Count > 0 && CanCombine(windows[^1].PElems, windows[^1].Length, window.PElems, window.Length))
                {
                    windows[^1] = window with { PElems = windows[^1].PElems, Length = window.Length + windows[^1].Length };
                }
                else
                {
                    windows.Add(window);
                }
            }

            return new UpdateWindows<T>(windows, _totalWindowLengthUsed);
        }

        public void Clear()
        {
            _olderWindows.Clear();
            _currentWindow = default;
            _totalWindowLengthUsed = 0;
        }
    }
}
