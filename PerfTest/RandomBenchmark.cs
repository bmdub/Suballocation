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
        public string RentalCount { get; private set; } = "";
        public string LengthRented { get; private set; } = "";
        public string LengthRentedActual { get; private set; } = "";
        public string WindowTotalLengthAvg { get; private set; } = "";
        public string WindowSpreadMax { get; private set; } = "";
        public string WindowSpreadAvg { get; private set; } = "";
        public string WindowCountAvg { get; private set; } = "";
        private ISuballocator<T> _suballocator;
        private Random _random;
        private int _seed;
        private int _minSegmentLen;
        private int _maxSegmentLen;

        public RandomBenchmark(ISuballocator<T> suballocator, int seed, int minSegmentLen, int maxSegmentLen)
        {
            Allocator = suballocator.GetType().Name;
            Size = suballocator.SizeTotal.ToString("N0");
            _suballocator = suballocator;
            _seed = seed;
            _random = new Random(seed);
            _minSegmentLen = minSegmentLen;
            _maxSegmentLen = maxSegmentLen;
        }

        public unsafe override void PrepareIteration()
        {
            _random = new Random(_seed);
            _suballocator.Clear();
        }

        public unsafe override void RunIteration()
        {
            List<NativeMemorySegment<T>> segments = new List<NativeMemorySegment<T>>((int)_suballocator.LengthTotal);
            long rentalCount = 0;
            long lengthRented = 0;
            long lengthRentedActual = 0;
            long windowTotalLengthAvg = 0;
            long windowSpreadMax = 0;
            long windowSpreadAvg = 0;
            long windowCountAvg = 0;
            long windowSamples = 0;
            bool oom = false;
            var updateWindowTracker = new UpdateWindowTracker1<T>(.55);// Math.BitDecrement(1.0));

            try
            {
                while (lengthRented < _suballocator.LengthTotal / 2)
                {
                    var lengthToRent = _random.Next(_minSegmentLen, _maxSegmentLen + 1);
                    var seg = _suballocator.Rent(lengthToRent);
                    lengthRented += lengthToRent;
                    lengthRentedActual += seg.Length;
                    segments.Add(seg);
                }

                while (lengthRented < _suballocator.LengthTotal * 20)
                {
                    if (_random.Next(0, 2) == 0)
                    {
                        var lengthToRent = _random.Next(_minSegmentLen, _maxSegmentLen + 1);
                        var seg = _suballocator.Rent(lengthToRent);
                        rentalCount++;
                        lengthRented += lengthToRent;
                        lengthRentedActual += seg.Length;
                        segments.Add(seg);
                        updateWindowTracker.Register(seg);

                        if (lengthRented % 100 == 0)
                        {
                            var updateWindows = updateWindowTracker.BuildUpdateWindows();
                            updateWindowTracker.Clear();

                            windowTotalLengthAvg += updateWindows.TotalLength;
                            windowSpreadMax = Math.Max(windowSpreadMax, updateWindows.SpreadLength);
                            windowSpreadAvg += updateWindows.SpreadLength;
                            windowCountAvg += updateWindows.Windows.Count;
                            windowSamples++;

                           /* int wCount = 0;
                            for (int j = 0; j < 150; j++)
                            {
                                char c = ' ';

                                //Console.WriteLine("-" + ((long)updateWindows.Windows[0].PElems - (long)_suballocator.PElems));
                                //Console.WriteLine(((long)updateWindows.Windows[0].PElems - (long)_suballocator.PElems) / (double)_suballocator.SizeTotal * 150);
                                //Console.WriteLine((((long)updateWindows.Windows[^1].PElems - (long)_suballocator.PElems) + updateWindows.Windows[^1].Length) / (double)_suballocator.LengthTotal * 150);

                                if (((long)updateWindows.Windows[0].PElems - (long)_suballocator.PElems) / (double)_suballocator.SizeTotal * 150 <= j)
                                {
                                    if ((((long)updateWindows.Windows[^1].PElems - (long)_suballocator.PElems) + updateWindows.Windows[^1].Size) / (double)_suballocator.SizeTotal * 150 >= j ||
                                        wCount == 0)
                                    {
                                        c = 'W';
                                        wCount++;
                                    }
                                }

                                Console.Write(c);
                            }
                            Console.WriteLine();*/
                        }
                    }
                    else if(segments.Count > 1)
                    {
                        var swapIndex = _random.Next(0, segments.Count);
                        var returnSeg = segments[swapIndex];
                        segments[swapIndex] = segments[^1];
                        segments.RemoveAt(segments.Count - 1);

                        _suballocator.Return(returnSeg);
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                oom = true;
            }

            RentalCount = rentalCount.ToString("N0") + (oom ? "(OOM)" : "");
            LengthRented = lengthRented.ToString("N0");
            LengthRentedActual = lengthRentedActual.ToString("N0");

            if (windowSamples > 0)
            {
                WindowTotalLengthAvg = (windowTotalLengthAvg / windowSamples).ToString("N0");
                WindowSpreadMax = windowSpreadMax.ToString("N0");
                WindowSpreadAvg = (windowSpreadAvg / windowSamples).ToString("N0");
                WindowCountAvg = (windowCountAvg / windowSamples).ToString("N0");
            }
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }
}
