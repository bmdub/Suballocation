using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Suballocation;


namespace PerfTest
{
    class RandomBenchmark<T> : BenchmarkBase where T : unmanaged
    {
        public string Allocator { get; init; }
        public string Size { get; init; }
        public string LengthRented { get; private set; } = "";
        public string LengthRentedActual { get; private set; } = "";
        private ISuballocator<T> _suballocator;
        private Random _random;
        private int _seed;
        private int _maxLen;

        public RandomBenchmark(ISuballocator<T> suballocator, int seed, int maxLen)
        {
            Allocator = suballocator.GetType().Name;
            Size = suballocator.SizeTotal.ToString("N0");
            _suballocator = suballocator;
            _seed = seed;
            _random = new Random(seed);
            _maxLen = maxLen;
        }

        public unsafe override void PrepareIteration()
        {
            _random = new Random(_seed);
            _suballocator.Clear();
        }

        public unsafe override void RunIteration()
        {
            List<NativeMemorySegment<T>> segments = new List<NativeMemorySegment<T>>((int)_suballocator.LengthTotal);
            long lengthRented = 0;
            long lengthRentedActual = 0;

            try
            {
                while (lengthRented < _suballocator.LengthTotal / 4)
                {
                    var lengthToRent = _random.Next(1, _maxLen + 1);
                    var seg = _suballocator.Rent(lengthToRent);
                    lengthRented += lengthToRent;
                    lengthRentedActual += seg.Length;
                    segments.Add(seg);
                }

                for (int i = 0; i < _suballocator.LengthTotal * 1; i++)
                {
                    var lengthToRent = _random.Next(1, _maxLen + 1);
                    var seg = _suballocator.Rent(lengthToRent);
                    lengthRented += lengthToRent;
                    lengthRentedActual += seg.Length;
                    segments.Add(seg);

                    var swapIndex = _random.Next(0, segments.Count);
                    var returnSeg = segments[swapIndex];
                    segments[swapIndex] = segments[^1];
                    segments.RemoveAt(segments.Count - 1);

                    _suballocator.Return(returnSeg);
                }
            }
            catch (Exception ex)
            {
                lengthRentedActual = 0;
            }

            LengthRented = lengthRented.ToString("N0");
            LengthRentedActual = lengthRentedActual.ToString("N0");
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }
}
