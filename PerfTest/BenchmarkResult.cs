using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfTest
{
    internal class BenchmarkResult
    {
        private Dictionary<string, string> _valuesByName = new();

        public BenchmarkResult(IBenchmark benchmark)
        {
            Benchmark = benchmark;

            foreach (var pair in benchmark.GetColumnHeaders().Zip(benchmark.GetColumnValues()))
            {
                _valuesByName[pair.First] = pair.Second;
            }
        }

        public IBenchmark Benchmark { get; init; }

        public string GetValue(string columnName)
        {
            return _valuesByName[columnName];
        }

        public bool TryGetValue(string columnName, out string value)
        {
#pragma warning disable CS8601 // Possible null reference assignment.
            return _valuesByName.TryGetValue(columnName, out value);
#pragma warning restore CS8601 // Possible null reference assignment.
        }

        public IEnumerable<(string Header, int Length)> GetColumnMetadata()
        {
            foreach(var kvp in _valuesByName)
            {
                int len = Math.Max(kvp.Key.Length, kvp.Value.Length);

                yield return (kvp.Key, len);
            }
        }

        public IEnumerable<(string Header, string Value)> GetColumnValues()
        {
            return _valuesByName.Select(kvp => (kvp.Key, kvp.Value));
        }
    }
}
