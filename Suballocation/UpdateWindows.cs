using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Suballocation
{
    public class UpdateWindows<T> where T : unmanaged
    {
        public unsafe UpdateWindows(List<NativeMemorySegment<T>> windows)
        {
            Windows = windows;

            if(windows.Count == 0)
            {
                return;
            }

            SpreadLength = (windows[^1].PElems + windows[^1].Length) - windows[0].PElems;

            foreach(var window in windows)
            {
                TotalLength += window.Length;
            }

            TotalFillPercentage = 
        }

        public ImmutableList<NativeMemorySegment<T>> Windows { get; init; }

        public long SpreadLength { get; init; }
        public long SpreadSize => SpreadLength * Unsafe.SizeOf<T>();
        public double SpreadFillPercentage { get; init; }
        public long TotalLength { get; init; }
        public long TotalSize => TotalLength * Unsafe.SizeOf<T>();
        public double TotalFillPercentage { get; init; }
    }
}
