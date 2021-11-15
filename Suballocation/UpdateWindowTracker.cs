using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Suballocation
{
    //todo: keep running sum of segment length, and unfilled segment length. pass to updatewindows

    public class UpdateWindowTracker1<T> where T : unmanaged
    {
        private readonly Stack<NativeMemorySegment<T>> _windowsPrev = new();
        private readonly Stack<NativeMemorySegment<T>> _windowsNext = new();
        private readonly double _minimumFillPercentage;

        public UpdateWindowTracker1(double minimumFillPercentage)
        {
            _minimumFillPercentage = minimumFillPercentage;
        }

        public double MinimumFillPercentage => _minimumFillPercentage;

        public unsafe UpdateWindowTracker1(ISegment<T> segment)
        {
            bool havePrevWindow = _windowsPrev.TryPeek(out var prevWindow);
            bool haveNextWindow = _windowsNext.TryPeek(out var nextWindow);

            while(haveNextWindow && nextWindow.PElems < segment.PElems)
            {
                _windowsPrev.Push(_windowsNext.Pop());

                haveNextWindow = _windowsNext.TryPeek(out nextWindow);
            }

            while (havePrevWindow && prevWindow.PElems > segment.PElems)
            {
                _windowsNext.Push(_windowsPrev.Pop());

                haveNextWindow = _windowsPrev.TryPeek(out prevWindow);
            }

            if(haveNextWindow && CanCombine(segment.PElems, segment.Length, nextWindow.PElems, nextWindow.Length))
            {
                var window = _windowsNext.Pop();
                window = window with { PElems = segment.PElems, Length = window.Length + segment.Length };
                _windowsNext.Push(window);
                return;
            }

            if (havePrevWindow && CanCombine(prevWindow.PElems, prevWindow.Length, segment.PElems, segment.Length))
            { 
                var window = _windowsPrev.Pop();
                window = window with { PElems = window.PElems, Length = window.Length + segment.Length };
                _windowsPrev.Push(window);
                return;
            }

            _windowsPrev.Push(new NativeMemorySegment<T>() { PElems = segment.PElems, Length = segment.Length });
        }

        private unsafe bool CanCombine(T* pElemsPrev, long lengthPrev, T* pElemsNext, long lengthNext)
        {
            return (lengthNext + lengthPrev) / (pElemsNext + lengthNext - pElemsPrev) >= _minimumFillPercentage;
        }

        public unsafe UpdateWindows<T> BuildUpdateWindows()
        {
            foreach (var window in _windowsPrev)
            {
                _windowsNext.Push(window);
            }

            List<NativeMemorySegment<T>> windows = new List<NativeMemorySegment<T>>(_windowsNext.Count);

            foreach(var window in _windowsNext)
            {
                if(windows.Count > 0 && CanCombine(windows[^1].PElems, windows[^1].Length, window.PElems, window.Length))
                {
                    windows[^1] = window with { PElems = windows[^1].PElems, Length = window.Length + windows[^1].Length };
                }
                else
                {
                    windows.Add(window);
                }
            }

            return new UpdateWindows<T>(windows);
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

        static unsafe UpdateWindowTracker2()
        {
            _segmentComparer = Comparer<NativeMemorySegment<T>>.Create((a, b) => ((IntPtr)a.PElems).CompareTo((IntPtr)b.PElems));
        }

        public UpdateWindowTracker2(double minimumFillPercentage)
        {
            _minimumFillPercentage = minimumFillPercentage;
        }

        public double MinimumFillPercentage => _minimumFillPercentage;

        public unsafe UpdateWindowTracker2(ISegment<T> segment)
        {
            if(_currentWindow.Length == 0)
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
            if(_currentWindow.Length > 0)
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

            return new UpdateWindows<T>(windows);
        }

        public void Clear()
        {
            _olderWindows.Clear();
            _currentWindow = default;
        }
    }
}
