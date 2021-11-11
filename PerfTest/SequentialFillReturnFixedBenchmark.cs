using Suballocation;


namespace PerfTest
{
    class SequentialFillReturnFixedBenchmark<T> : BenchmarkBase where T : unmanaged
    {
        public string Allocator { get; init; }
        public string Size { get; init; }
        public long LengthRented { get; private set; }
        private ISuballocator<T> _suballocator;

        public SequentialFillReturnFixedBenchmark(ISuballocator<T> suballocator)
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
            List<NativeMemorySegment<T>> segments = new List<NativeMemorySegment<T>>((int)_suballocator.LengthTotal);

            for (int i = 0; i < _suballocator.LengthTotal; i++)
            {
                segments.Add(_suballocator.Rent(1));
                LengthRented += 1;
            }

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                _suballocator.Return(segments[i]);
            }
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }


}
