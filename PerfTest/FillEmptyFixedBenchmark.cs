using Suballocation;


namespace PerfTest
{
    class FillEmptyFixedBenchmark<T> : BenchmarkBase where T : unmanaged
    {
        public string Allocator { get; init; }
        public string Size { get; init; }
        public string LengthRented { get; private set; } = "";
        private ISuballocator<T> _suballocator;

        public FillEmptyFixedBenchmark(ISuballocator<T> suballocator)
        {
            Allocator = suballocator.GetType().Name;
            Size = suballocator.LengthBytesTotal.ToString("N0");
            _suballocator = suballocator;
        }

        public unsafe override void PrepareIteration()
        {
            _suballocator.Clear();
        }

        public unsafe override void RunIteration()
        {
            List<NativeMemorySegment<T>> segments = new List<NativeMemorySegment<T>>((int)_suballocator.LengthTotal);
            long lengthRented = 0;

            for (int i = 0; i < _suballocator.LengthTotal; i++)
            {
                segments.Add(_suballocator.Rent(1));
                lengthRented += 1;
            }

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                _suballocator.Return(segments[i]);
            }

            LengthRented = lengthRented.ToString("N0");
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }


}
