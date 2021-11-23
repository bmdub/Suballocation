using SkiaSharp;
using Suballocation;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PerfTest
{
    interface IBenchmark
    {
        IEnumerable<string> GetColumnHeaders();
        IEnumerable<string> GetColumnValues();
        void ShowImage();
    }

    internal class Benchmark<T> : IBenchmark, IDisposable where T : unmanaged
    {
        public string Name { get => this.GetType().Name.Replace("Benchmark`1", ""); }
        public string Allocator { get; init; }
        public long DurationMs { get; private set; }
        public long RentalCount { get; private set; }
        public long LengthRentedActual { get; private set; }
        public long WindowTotalLengthAvg { get; private set; }
        public long WindowSpreadMax { get; private set; }
        public long WindowSpreadAvg { get; private set; }
        public long WindowCountAvg { get; private set; }
        public bool RanOutOfSpace { get; private set; }

        private ISuballocator<T> _suballocator;
        private Random _random;
        private Stopwatch _stopwatch;
        private int _seed;
        private long _windowSamples;
        private long _windowTotalLengthSum;
        private long _windowSpreadSum;
        private long _windowCountSum;
        private SKImageInfo _imageInfo;
        private SKSurface _surface;
        private SKPaint _paintFore;
        private SKPaint _paintBack;
        private int _y;

        public Benchmark(ISuballocator<T> suballocator, int seed, int imageWidth, int imageHeight)
        {
            Allocator = suballocator.GetType().Name;
            _suballocator = suballocator;
            _seed = seed;
            _random = new Random(seed);
            _stopwatch = new Stopwatch();

            _imageInfo = new SKImageInfo(imageWidth, imageHeight);
            _surface = SKSurface.Create(_imageInfo);
            _surface.Canvas.Clear(SKColors.Black);
            _paintFore = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Fill,
                //TextAlign = SKTextAlign.Center,
                //TextSize = 24
            };
            _paintBack = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
            };

            _y = _imageInfo.Height - 1;
        }

        public unsafe BenchmarkResult Run(
            float countPct,
            int minSegmentLenInitial,
            int minSegmentLenFinal,
            int maxSegmentLenInitial,
            int maxSegmentLenFinal,
            double desiredFillRatio,
            double youthReturnFactor,
            UpdateWindowTracker1<T> windowTracker,
            int updatesPerWindow)
        {
            long totalLengthToRent = (long)(countPct * _suballocator.LengthTotal);
            long lengthRented = 0;
            long lengthRentedSum = 0;
            long lengthRentedWindow = 0;
            int minSegmentLen = minSegmentLenInitial;
            int maxSegmentLen = maxSegmentLenInitial;

            windowTracker.Clear();
            List<NativeMemorySegment<T>> segments = new List<NativeMemorySegment<T>>((int)totalLengthToRent);

            _stopwatch.Restart();

            .//todo: put all add/removed segs in lists; do tracking and drawing at end
            //todo also: maybe try writing to segment?

            try
            {
                while (lengthRentedSum < totalLengthToRent)
                {
                    minSegmentLen = (int)((lengthRentedSum / (double)totalLengthToRent) * (minSegmentLenFinal - minSegmentLenInitial) + minSegmentLenInitial);
                    maxSegmentLen = (int)((lengthRentedSum / (double)totalLengthToRent) * (maxSegmentLenFinal - maxSegmentLenInitial) + maxSegmentLenInitial);

                    double fillRatio = lengthRented / (double)_suballocator.LengthTotal;

                    var fillRatioDiff = desiredFillRatio - fillRatio;

                    var threshold = .5 + fillRatioDiff;

                    if (_random.NextDouble() < threshold)
                    {
                        var lengthToRent = _random.Next(minSegmentLen, maxSegmentLen + 1);
                        var seg = _suballocator.Rent(lengthToRent);
                        lengthRentedSum += lengthToRent;
                        lengthRentedWindow += lengthToRent;
                        LengthRentedActual += seg.Length;
                        lengthRented += seg.Length;
                        segments.Add(seg);
                        windowTracker.Register(seg);

                        if (_y > 0)
                        {
                            double scale = _imageInfo.Width / (double)_suballocator.LengthTotal;
                            var pixelStartX = ((long)seg.PElems - (long)_suballocator.PElems) / Unsafe.SizeOf<T>() * scale;
                            //Console.WriteLine(pixelStartX);
                            var pixelLengthX = seg.Length * scale;
                            if (pixelLengthX < 1)
                                pixelLengthX = 1;
                            _surface.Canvas.DrawRect((float)pixelStartX, 0, (float)pixelLengthX, _y, _paintFore);
                        }

                        if (lengthRentedWindow >= updatesPerWindow)
                        {
                            lengthRentedWindow = 0;

                            var updateWindows = windowTracker.BuildUpdateWindows();
                            windowTracker.Clear();

                            _windowTotalLengthSum += updateWindows.TotalLength;
                            WindowSpreadMax = Math.Max(WindowSpreadMax, updateWindows.SpreadLength);
                            _windowSpreadSum += updateWindows.SpreadLength;
                            _windowCountSum += updateWindows.Windows.Count;
                            _windowSamples++;

                            _y--;
                        }
                    }
                    else if (segments.Count > 1)
                    {
                        var swapIndex = _random.Next(0, segments.Count);

                        //var swapIndex = (int)(Math.Log2(_random.NextDouble() * 7.0 + 1) / 3 * segments.Count);
                        //swapIndex = (int)((Math.Log(_random.NextDouble() * Math.E) + 4) / (1 + 4) * segments.Count);
                        //while(swapIndex < 0 || swapIndex >= segments.Count)
                        //swapIndex = (int)((Math.Log(_random.NextDouble() * Math.E) + 4) / (1 + 4) * segments.Count);

                        swapIndex = (int)((1.0 - (_random.NextDouble() * youthReturnFactor)) * segments.Count);

                        var seg = segments[swapIndex];
                        segments[swapIndex] = segments[^1];
                        segments.RemoveAt(segments.Count - 1);

                        if (_y > 0)
                        {
                            double scale = _imageInfo.Width / (double)_suballocator.LengthTotal;
                            var pixelStartX = ((long)seg.PElems - (long)_suballocator.PElems) / Unsafe.SizeOf<T>() * scale;
                            var pixelLengthX = seg.Length * scale;
                            if (pixelLengthX < 1)
                                pixelLengthX = 1;
                            _surface.Canvas.DrawRect((float)pixelStartX, 0, (float)pixelLengthX, _y, _paintBack);
                        }

                        _suballocator.Return(seg);
                        lengthRented -= seg.Length;
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                RanOutOfSpace = true;
            }

            _stopwatch.Stop();

            if (_windowSamples > 0)
            {
                WindowTotalLengthAvg = _windowTotalLengthSum / _windowSamples;
                WindowSpreadAvg = _windowSpreadSum / _windowSamples;
                WindowCountAvg = _windowCountSum / _windowSamples;
            }

            DurationMs = _stopwatch.ElapsedMilliseconds;//.ToString("0.###");

            if (_y > 0)
            {
                _surface.Canvas.DrawRect(0, 0, _imageInfo.Width, _y - 1, _paintBack);
            }

            return new BenchmarkResult(this);
        }

        public void ShowImage()
        {
            // Save the file
            using (var image = _surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite($"{Allocator}.png"))
            {
                data.SaveTo(stream);
            }

            // Open the file
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = $"{Allocator}.png",
            };
            process.StartInfo = startInfo;
            process.Start();
        }

        public IEnumerable<string> GetColumnHeaders()
        {
            return GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Select(prop => prop.Name);
        }

        public IEnumerable<string> GetColumnValues()
        {
            foreach (var prop in GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                var value = prop.GetValue(this)?.ToString();

                if (value != null)
                    yield return value;
            }
        }

        public void Dispose()
        {
            _surface.Dispose();
        }
    }
}
