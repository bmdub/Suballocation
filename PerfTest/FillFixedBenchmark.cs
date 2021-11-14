using Suballocation;


namespace PerfTest
{
    class FillFixedBenchmark<T> : BenchmarkBase where T : unmanaged
    {
        public string Allocator { get; init; }
        public string Size { get; init; }
        public string LengthRented { get; private set; } = "";
        private ISuballocator<T> _suballocator;

        public FillFixedBenchmark(ISuballocator<T> suballocator)
        {
            Allocator = suballocator.GetType().Name;
            Size = suballocator.SizeTotal.ToString("N0");
            _suballocator = suballocator;
        }

        public unsafe override void PrepareIteration()
        {
            _suballocator.Clear();
        }

        public unsafe override void RunIteration()
        {
            long lengthRented = 0;

            for (int i = 0; i < _suballocator.LengthTotal; i++)
            {
                _suballocator.Rent(1);
                lengthRented += 1;
            }

            LengthRented = lengthRented.ToString("N0");
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }
}
