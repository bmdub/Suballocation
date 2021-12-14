using ScottPlot;
using System.Diagnostics;

namespace PerfTest
{
    /// <summary>
    /// Contains extensions to a set of benchmark results for displaying results.
    /// </summary>
    internal static class BenchmarkResults
    {
        /// <summary>Displays all of the allocation pattern images.</summary>
        public static void ShowPatternImages(this IEnumerable<Benchmark> results)
        {
            foreach (var result in results)
                result.ShowPatternImage();
        }

        /// <summary>Shows a grouped bar graph.</summary>
        /// <param name="results"></param>
        /// <param name="name">The unique name to use for this graph and its file.</param>
        /// <param name="imageWidth">The image width.</param>
        /// <param name="imageHeight">The image height.</param>
        /// <param name="keyNameGroup">The key of the benchmark value to use for grouping.</param>
        /// <param name="keyNameLabel">The key of the benchmark value to use for labeling each bar.</param>
        /// <param name="keyNameValue">The key of the benchmark value to use for the value of each bar.</param>
        public static void ShowGroupedBarGraph(this IEnumerable<Benchmark> results, string imageFolder, string name, int imageWidth, int imageHeight, string keyNameGroup, string keyNameLabel, string keyNameValue)
        {
            // Create the image file.
            var fileName = Path.Combine(imageFolder, $"{name}.png");

            var plot = new Plot(imageWidth, imageHeight);

            var groups = results.GroupBy(result => result.GetStat(keyNameGroup));

            plot.AddBarGroups(
                results.Select(result => result.GetStat(keyNameLabel)).Distinct().ToArray(),
                groups.Select(group => group.Key).ToArray(),
                groups.Select(group => group.Select(result => double.Parse(result.GetStat(keyNameValue))).ToArray()).ToArray(),
                groups.Select(group => group.Select(result => 0.0).ToArray()).ToArray());
            plot.Legend(location: Alignment.UpperLeft);

            plot.YLabel(keyNameValue);
            plot.XLabel(keyNameLabel);

            plot.SetAxisLimits(yMin: 0);

            plot.SaveFig(fileName);

            // Open the image file.
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = fileName,
            };
            process.StartInfo = startInfo;
            process.Start();
        }

        /// <summary>Shows a bar graph.</summary>
        /// <param name="results"></param>
        /// <param name="name">The unique name to use for this graph and its file.</param>
        /// <param name="imageWidth">The image width.</param>
        /// <param name="imageHeight">The image height.</param>
        /// <param name="keyNameX">The key of the benchmark value to use for the horizontal column.</param>
        /// <param name="keyNameY">The key of the benchmark value to use for the vertical column.</param>
        public static void ShowBarGraph(this IEnumerable<Benchmark> results, string imageFolder, string name, int imageWidth, int imageHeight, string keyNameX, string keyNameY)
        {
            // Create the image file.
            var fileName = Path.Combine(imageFolder, $"{name}.png");

            var plot = new Plot(imageWidth, imageHeight);

            plot.XTicks(results.Select(result => result.GetStat(keyNameX)).ToArray());
            plot.XLabel(keyNameX);
            plot.YLabel(keyNameY);

            var bar = plot.AddBar(results.Select(result => double.Parse(result.GetStat(keyNameY))).ToArray());
            bar.ShowValuesAboveBars = true;

            plot.SetAxisLimits(yMin: 0);

            plot.SaveFig(fileName);

            // Open the image file.
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = fileName,
            };
            process.StartInfo = startInfo;
            process.Start();
        }

        /// <summary>Outputs benchmark values to console as a table.</summary>
        /// <param name="results"></param>
        /// <param name="name">The name for this table.</param>
        public static void ShowConsole(this IEnumerable<Benchmark> results, string name)
        {
            Console.WriteLine(name);

            // Gather display length requirements for each column.
            var lengthsByHeader =
                results.SelectMany(result => result.GetColumnMetadata())
                    .GroupBy(md => md.Key)
                    .Select(group => group.Aggregate((a, b) => (a.Key, Math.Max(a.Length, b.Length))))
                    .ToDictionary(md => md.Key, md => md.Length);

            Console.WriteLine();

            foreach(var kvp in lengthsByHeader)
            {
                Console.Write(kvp.Key.PadLeft(kvp.Value));
                Console.Write(" | ");
            }

            Console.WriteLine();

            // Write the benchmark values.
            foreach (var result in results)
            {
                foreach (var pair in result.GetStats())
                {
                    string str = "";

                    if(lengthsByHeader.TryGetValue(pair.Key, out var length) == true)
                    {
                        str = pair.Value;
                    }

                    Console.Write(str.PadRight(length));
                    Console.Write(" | ");
                }

                Console.WriteLine();
            }

            Console.WriteLine();
        }
    }
}
