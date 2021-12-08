﻿using SkiaSharp;
using Suballocation;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Suballocation.Suballocators;
using Suballocation.Trackers;

namespace PerfTest
{
    /// <summary>
    /// Represents benchmark results.
    /// </summary>
    internal class Benchmark
    {
        private readonly string _imageFileName;
        private readonly Dictionary<string, string> _valuesByName = new();

        /// <summary></summary>
        /// <param name="imageFileName">The file name (without extension) to use for the pattern image.</param>
        /// <param name="valuesByName">Dictionary of benchmark names->results.</param>
        public Benchmark(string imageFileName, Dictionary<string, string> valuesByName)
        {
            _imageFileName = imageFileName;
            _valuesByName = valuesByName;
        }

        /// <summary>
        /// Runs a benchmark.
        /// </summary>
        /// <typeparam name="TSeg">Allocation element type.</typeparam>
        /// <param name="imageFolder">The folder in which to place the images.</param>
        /// <param name="name">The name of the benchmark. This along with the tag uniquely identify the benchmark.</param>
        /// <param name="tag">A tag for categorization. This along with the name uniquely identify the benchmark.</param>
        /// <param name="suballocator">A suballocator instance to use.</param>
        /// <param name="seed">A seed for the randomizer.</param>
        /// <param name="imageWidth">The width of the resulting pattern image.</param>
        /// <param name="imageHeight">The height of the resulting pattern image.</param>
        /// <param name="totalLengthToRent">The length of elements to rent in total.</param>
        /// <param name="minSegmentLenInitial">The minimum segment length to rent, initially, before linear interpolation.</param>
        /// <param name="minSegmentLenFinal">The minimum segment length to rent, finally, after linear interpolation.</param>
        /// <param name="maxSegmentLenInitial">The maximum segment length to rent, initially, before linear interpolation.</param>
        /// <param name="maxSegmentLenFinal">The maximum segment length to rent, finally, after linear interpolation.</param>
        /// <param name="desiredFillPercentage">The percentage from 0 to 1 of the suballocator's length to fill with rentals.</param>
        /// <param name="youthReturnFactor">A weighting from 0 to 1 indicating propensity for newer segments to be returned to the pool over older ones.</param>
        /// <param name="updateWindowFillPercentage">The minimum update rate for a portion of the buffer to be coalesced into an update window.</param>
        /// <param name="updatesPerWindow">The number of rents/returns to assign to each "update" or build of the update windows.</param>
        /// <returns></returns>
        public static unsafe Benchmark Run<TSeg>(
            string imageFolder,
            string name,
            string tag,
            ISuballocator<TSeg> suballocator,
            int seed,
            int imageWidth,
            int imageHeight,
            long totalLengthToRent,
            int minSegmentLenInitial,
            int minSegmentLenFinal,
            int maxSegmentLenInitial,
            int maxSegmentLenFinal,
            double desiredFillPercentage,
            double youthReturnFactor,
            double updateWindowFillPercentage,
            int updatesPerWindow,
            bool defragment,
            long fragmentBucketLength)
            where TSeg : unmanaged
        {
            long lengthRentedCurrent = 0;
            long lengthRentedTotal = 0;
            long countRentedWindow = 0;
            long windowSetLengthTotal = 0;
            long windowSetSpreadMax = 0;
            long windowSetSpreadTotal = 0;
            long windowCountTotal = 0;
            long windowBuilds = 0;
            long elapsedTicks = 0;
            int minSegmentLen = minSegmentLenInitial;
            int maxSegmentLen = maxSegmentLenInitial;
            bool outOfMemory = false;

            var imageInfo = new SKImageInfo(imageWidth, imageHeight);
            var surface = SKSurface.Create(imageInfo);
            double imageScale = imageInfo.Width / (double)suballocator.Length;
            var paintOccupied = new SKPaint { Color = SKColors.Red };
            var paintRemoved = new SKPaint { Color = SKColors.Black };
            var paintMovedFrom = new SKPaint { Color = SKColors.PaleVioletRed };
            var imageYOffset = imageInfo.Height - 1;
            surface.Canvas.Clear(SKColors.Black);

            List<NativeMemorySegment<TSeg, EmptyStruct>> segments = new();
            Dictionary<long, int> segmentsByOffset = new();
            List<(int Type, NativeMemorySegment<TSeg, EmptyStruct> Segment, int SegmentIndex)> windowSegments = new(updatesPerWindow);
            var windowTracker = new UpdateWindowTracker<TSeg>(updateWindowFillPercentage);
            var random = new Random(seed);

            var fragTracker = new FragmentationTracker<TSeg>(suballocator.Length, fragmentBucketLength);

            Console.WriteLine($"Running {name} ({tag})...");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // Rent/return segment loop
            while (lengthRentedTotal < totalLengthToRent)
            {
                // Randomly choose whether to rent a segment or return an existing one.
                // Weight this decision by the current and desired fill ratio for the buffer.
                double currentFillRatio = lengthRentedCurrent / (double)suballocator.Length;

                if (currentFillRatio < desiredFillPercentage && random.NextDouble() < .75)
                {
                    // Rent a segment of random size.
                    // Size range determined by linearly interpolating input min/max segment lengths over time.
                    minSegmentLen = (int)(lengthRentedTotal / (double)totalLengthToRent * (minSegmentLenFinal - minSegmentLenInitial) + minSegmentLenInitial);
                    maxSegmentLen = (int)(lengthRentedTotal / (double)totalLengthToRent * (maxSegmentLenFinal - maxSegmentLenInitial) + maxSegmentLenInitial);

                    var lengthToRent = random.Next(minSegmentLen, maxSegmentLen + 1);

                    if (suballocator.TryRent(lengthToRent, out var segment) == false)
                    {
                        outOfMemory = true;
                        break;
                    }

                    segments.Add(segment);
                    windowSegments.Add((1, segment, segments.Count - 1));
                    segmentsByOffset[segment.RangeOffset] = segments.Count - 1;

                    lengthRentedCurrent += segment.Length;
                    lengthRentedTotal += segment.Length;
                    countRentedWindow++;

                    windowTracker.TrackRental(segment);
                    fragTracker.TrackUpdate(segment);

                    // After every N rentals, "apply" them; coalesce into update windows.
                    if (countRentedWindow >= updatesPerWindow)
                    {
                        stopwatch.Stop();
                        elapsedTicks += stopwatch.ElapsedTicks;

                        // Do some defragging
                        if (defragment)
                        {
                            var fragmented = fragTracker.GetFragmentedSegments(.1).ToList();
                            var removed = new List<(int SegmentIndex, long Length)>();
                            foreach (var seg in fragmented)
                            {
                                fragTracker.TrackReturn(seg);
                                if (fragTracker.GetFragmentedSegments(.1).Where(s => s.RangeOffset == seg.RangeOffset).Any())
                                    Debugger.Break();

                                var index = segmentsByOffset[seg.RangeOffset];

                                windowSegments.Add((3, seg, index));

                                suballocator.Return(seg);

                                segmentsByOffset.Remove(seg.RangeOffset);
                                removed.Add((index, seg.Length));
                            }

                            foreach (var removal in removed)
                            {
                                if (suballocator.TryRent(removal.Length, out var movedSegment) == false)
                                {
                                    outOfMemory = true;
                                    break;
                                }

                                windowSegments.Add((1, movedSegment, removal.SegmentIndex));

                                segmentsByOffset[movedSegment.RangeOffset] = removal.SegmentIndex;
                                segments[removal.SegmentIndex] = movedSegment;
                                windowTracker.TrackRental(movedSegment);
                                fragTracker.TrackUpdate(movedSegment);
                            }

                            if (outOfMemory)
                            {
                                break;
                            }
                        }

                        var updateWindows = windowTracker.BuildUpdateWindows();

                        windowSetLengthTotal += updateWindows.TotalLength;
                        windowSetSpreadMax = Math.Max(windowSetSpreadMax, updateWindows.SpreadLength);
                        windowSetSpreadTotal += updateWindows.SpreadLength;
                        windowCountTotal += updateWindows.Count;
                        windowBuilds++;

                        windowTracker.Clear();

                        // If there's room left, display a visual line of the new alocations / returns.
                        if (imageYOffset >= 0)
                        {
                            var pixels = new byte[imageWidth];

                            for (int i = 0; i < windowSegments.Count; i++)
                            {
                                var offsetX = ((long)windowSegments[i].Segment.PSegment - (long)suballocator.PElems) / Unsafe.SizeOf<TSeg>() * imageScale;
                                var width = windowSegments[i].Segment.Length * imageScale;

                                // The value of the pixel is defined by the last window to overlap it.
                                for (int j = (int)offsetX; j < offsetX + width; j++)
                                {
                                    //if(pixels[j] != true)
                                    pixels[j] = (byte)windowSegments[i].Type;
                                }
                            }

                            for (int i = 0; i < pixels.Length; i++)
                            {
                                if (pixels[i] == 0)
                                {
                                    continue;
                                }

                                if (pixels[i] == 3)
                                {
                                    surface.Canvas.DrawRect(i, 0, 1, imageYOffset, paintRemoved);
                                    surface.Canvas.DrawRect(i, imageYOffset, 1, 1, paintMovedFrom);
                                }
                                else
                                {
                                    surface.Canvas.DrawRect(i, 0, 1, imageYOffset, pixels[i] == 1 ? paintOccupied : paintRemoved);
                                }
                            }

                            imageYOffset--;
                        }

                        countRentedWindow = 0;
                        windowSegments.Clear();

                        GC.Collect(3, GCCollectionMode.Forced, true);

                        stopwatch.Restart();
                    }
                }
                else if (segments.Count > 1)
                {
                    // Return a random segment, weighted by age.

                    //var swapIndex = (int)(Math.Log2(_random.NextDouble() * 7.0 + 1) / 3 * segments.Count);
                    //swapIndex = (int)((Math.Log(_random.NextDouble() * Math.E) + 4) / (1 + 4) * segments.Count);
                    //while(swapIndex < 0 || swapIndex >= segments.Count)
                    //swapIndex = (int)((Math.Log(_random.NextDouble() * Math.E) + 4) / (1 + 4) * segments.Count);
                    var swapIndex = (int)((1.0 - (random.NextDouble() * youthReturnFactor)) * segments.Count);

                    var segment = segments[swapIndex];
                    segments[swapIndex] = segments[^1];
                    segments.RemoveAt(segments.Count - 1);

                    windowSegments.Add((2, segment, segments.Count - 1));

                    if (swapIndex < segments.Count)
                    {
                        segmentsByOffset[segments[swapIndex].RangeOffset] = swapIndex;
                    }

                    fragTracker.TrackReturn(segment);
                    segmentsByOffset.Remove(segment.RangeOffset);
                    suballocator.Return(segment);

                    lengthRentedCurrent -= segment.Length;
                }
            }

            stopwatch.Stop();

            // Collect statistics
            Dictionary<string, string> valuesByName = new();

            valuesByName["Name"] = name;
            valuesByName["Tag"] = tag;
            valuesByName["OOM"] = "yes";
            valuesByName["Duration (ms)"] = "0";
            valuesByName["Window Set Length (avg)"] = "0";
            valuesByName["Window Set Spread (avg)"] = "0";
            valuesByName["Window Set Spread (max)"] = "0";
            valuesByName["Window Count (avg)"] = "0";

            if (outOfMemory == false)
            {
                valuesByName["OOM"] = "no";
                valuesByName["Duration (ms)"] = new TimeSpan(elapsedTicks).TotalMilliseconds.ToString("0.###");

                if (windowBuilds > 0)
                {
                    valuesByName["Window Set Length (avg)"] = (windowSetLengthTotal / windowBuilds).ToString("N0");
                    valuesByName["Window Set Spread (avg)"] = (windowSetSpreadTotal / windowBuilds).ToString("N0");
                    valuesByName["Window Set Spread (max)"] = windowSetSpreadMax.ToString("N0");
                    valuesByName["Window Count (avg)"] = (windowCountTotal / windowBuilds).ToString("N0");
                }

                elapsedTicks += stopwatch.ElapsedTicks;
            }

            // If we didn't fill up the image, then block out the ends of the bars we made.
            surface.Canvas.DrawRect(0, 0, imageInfo.Width, imageYOffset - 1, paintRemoved);

            // Save the image file
            string imageFileName = Path.Combine(imageFolder, $"{tag}.{name}.png");
            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(imageFileName))
            {
                data.SaveTo(stream);
            }

            surface.Dispose();

            return new Benchmark(imageFileName, valuesByName);
        }

        /// <summary>Opens the allocation pattern image file.</summary>
        /// <returns></returns>
        public void ShowPatternImage()
        {
            // Open the image file
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = _imageFileName,
            };

            process.StartInfo = startInfo;
            process.Start();
        }

        /// <summary>Returns the given stat value by name.</summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string GetStat(string key)
        {
            return _valuesByName[key];
        }

        /// <summary>Returns the given stat value by name.</summary>
        /// <param name="key"></param>
        /// <returns>True if found.</returns>
        public bool TryGetStat(string key, out string? value)
        {
            return _valuesByName.TryGetValue(key, out value);
        }

        /// <summary>Gets each key/value pair.</summary>
        /// <returns></returns>
        public IEnumerable<(string Key, string Value)> GetStats()
        {
            return _valuesByName.Select(kvp => (kvp.Key, kvp.Value));
        }

        /// <summary>Gets the maximum length of each key/value pair.</summary>
        /// <returns></returns>
        public IEnumerable<(string Key, int Length)> GetColumnMetadata()
        {
            foreach (var kvp in _valuesByName)
            {
                int len = Math.Max(kvp.Key.Length, kvp.Value.Length);

                yield return (kvp.Key, len);
            }
        }
    }
}
