
using Suballocation;


namespace PerfTest
{
    class FillVariableBenchmark<T> : BenchmarkBase where T : unmanaged
    {
        public string Allocator { get; init; }
        public string Size { get; init; }
        public string LengthRented { get; private set; } = "";
        private ISuballocator<T> _suballocator;
        private Random _random;
        private int _maxLen;

        public FillVariableBenchmark(ISuballocator<T> suballocator, int seed, int maxLen)
        {
            Allocator = suballocator.GetType().Name;
            Size = suballocator.SizeTotal.ToString("N0");
            _suballocator = suballocator;
            _random = new Random(seed);
            _maxLen = maxLen;
        }

        public unsafe override void PrepareIteration()
        {
            _suballocator.Clear();
        }

        public unsafe override void RunIteration()
        {
            long lengthRented = 0;

            try
            {
                for (int i = 0; ; i++)
                {
                    var seg = _suballocator.Rent(_random.Next(1, _maxLen + 1));
                    lengthRented += seg.Length;
                }
            }
            catch (Exception)
            {

            }

            LengthRented = lengthRented.ToString("N0");
        }

        public override void Dispose()
        {
            _suballocator.Dispose();
        }
    }
}
