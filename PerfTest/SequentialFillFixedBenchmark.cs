using Suballocation;


namespace PerfTest
{
    class SequentialFillFixedBenchmark<T> : BenchmarkBase where T : unmanaged
    {
        public string Allocator { get; init; }
        public string Size { get; init; }
        public long LengthRented { get; private set; }
        private ISuballocator<T> _suballocator;

        public SequentialFillFixedBenchmark(ISuballocator<T> suballocator)
        {
            Allocator = suballocator.GetType().Name;
            Size = suballocator.SizeTotal.ToString("N0");
            _suballocator = suballocator;
        }

        public unsafe override void PrepareIteration()
        {
            _suballocator.Clear();
            LengthRented = 0;
        }

        public unsafe override void RunIteration()
        {
            for (int i = 0; i < _suballocator.LengthTotal; i++)
            {
                _suballocator.Rent(1);
                LengthRented += 1;
            }
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }
}
