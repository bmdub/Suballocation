using ScottPlot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfTest
{
    internal static class BenchmarkResults
    {
        public static void WriteToGroupedBarGraph(this IEnumerable<BenchmarkResult> results)
        {
            var plt = new ScottPlot.Plot(1200, 400);

            var groups = results.GroupBy(result => result.GetValue("Allocator"));

            // add the grouped bar plots and show a legend
            plt.AddBarGroups(
                results.Select(result => result.GetValue("Name")).Distinct().ToArray(),
                groups.Select(group => group.Key).ToArray(),
                groups.Select(group => group.Select(result => double.Parse(result.GetValue("DurationMs"))).ToArray()).ToArray(),
                groups.Select(group => group.Select(result => 0.0).ToArray()).ToArray());
            plt.Legend(location: Alignment.UpperLeft);

            // adjust axis limits so there is no padding below the bar graph
            plt.SetAxisLimits(yMin: 0);
            plt.YLabel("milliseconds");
            plt.SaveFig("bar_group.png");



            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "bar_group.png",
            };
            process.StartInfo = startInfo;
            process.Start();
        }

        public static void WriteToBarGraph(this IEnumerable<BenchmarkResult> results, string name, string xLabel, string yLabel, 
            Func<BenchmarkResult, string> labelSelector, Func<BenchmarkResult, double> valueSelector)
        {
            var plt = new ScottPlot.Plot(1000, 400);

            //var bar = plt.AddBar(results.Select(result => double.Parse(result.GetValue("DurationMs"))).ToArray());
            plt.XTicks(results.Select(result => labelSelector(result)).ToArray());
            plt.XLabel(xLabel);
            plt.YLabel(yLabel);

            var bar = plt.AddBar(results.Select(result => valueSelector(result)).ToArray());
            bar.ShowValuesAboveBars = true;

            // adjust axis limits so there is no padding below the bar graph
            plt.SetAxisLimits(yMin: 0);

            plt.SaveFig($"{name}.png");

            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = $"{name}.png",
            };
            process.StartInfo = startInfo;
            process.Start();
        }

        public static void WriteToConsole(this IEnumerable<IGrouping<string, BenchmarkResult>> resultGroups)
        {
            foreach(var group in resultGroups)
            {
                Console.WriteLine();
                group.WriteToConsole();
            }
        }

        public static void WriteToConsole(this IEnumerable<BenchmarkResult> results)
        {
            var lengthsByHeader =
                results.SelectMany(result => result.GetColumnMetadata())
                    .GroupBy(md => md.Header)
                    .Select(group => group.Aggregate((a, b) => (a.Header, Math.Max(a.Length, b.Length))))
                    .ToDictionary(md => md.Header, md => md.Length);

            Console.WriteLine();

            foreach(var kvp in lengthsByHeader)
            {
                Console.Write(kvp.Key.PadLeft(kvp.Value));
                Console.Write(" | ");
            }

            Console.WriteLine();

            foreach (var result in results)
            {
                foreach (var pair in result.GetColumnValues())
                {
                    string str = "";

                    if(lengthsByHeader.TryGetValue(pair.Header, out var length) == true)
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
