using System.Diagnostics;

namespace PerfTest
{
    abstract class BenchmarkBase : IDisposable
    {
        public string Name { get => this.GetType().Name.Replace("Benchmark`1", ""); }
        public string DurationMs { get; private set; } = "";

        public abstract void PrepareIteration();

        public abstract void RunIteration();

        public abstract void Dispose();

        public BenchmarkResult Run(int iterations)
        {
            Stopwatch stopwatch = new Stopwatch();

            Console.WriteLine($"Running {Name} warmup...");

            for (int i = 0; i < iterations; i++)
            {
                PrepareIteration();

                RunIteration();
            }

            Console.WriteLine($"Running {Name}...");

            for (int i = 0; i < iterations; i++)
            {
                PrepareIteration();

                stopwatch.Start();

                RunIteration();

                stopwatch.Stop();
            }

            DurationMs = new TimeSpan(stopwatch.ElapsedTicks / iterations).TotalMilliseconds.ToString("0.###");

            var result = new BenchmarkResult(this);

            Dispose();

            return result;
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
    }
}
