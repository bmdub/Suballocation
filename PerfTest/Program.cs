using Suballocation;
using Suballocation.Suballocators;

namespace PerfTest;

public partial class Program
{
    private const int _imageWidth = 1024;
    private const int _imageHeight = 1024;
    private static string _imageFolder = "../../../GeneratedImages/";

    static unsafe void Main(string[] args)
    {
        // Delete any old images first.
        _imageFolder = Path.Combine(Environment.CurrentDirectory, _imageFolder);
        foreach (var file in Directory.GetFiles(_imageFolder, "*.png"))
        {
            File.Delete(file);
        }

        // For instantiating suballocators, one at a time.
        IEnumerable<ISuballocator<T>> GetSuballocators<T>(long length, long blockLength) where T : unmanaged
        {
            yield return new SequentialBlockSuballocator<T>(length, blockLength);
            yield return new BuddySuballocator<T>(length, blockLength);
            yield return new DirectionalBlockSuballocator<T>(length, blockLength);
        }

        List<Benchmark> benchmarks = new List<Benchmark>();

        // Test random allocation lengths with a large buffer...
        benchmarks.AddRange(
            GetSuballocators<SomeStruct>(length: 1L << 23, blockLength: 32)
                .Select(suballocator =>
                    Benchmark.Run(
                        imageFolder: _imageFolder,
                        name: suballocator.GetType().Name.Replace("`1", ""),
                        tag: "Random Large",
                        suballocator: suballocator,
                        seed: 0,
                        imageWidth: _imageWidth, imageHeight: _imageHeight,
                        totalLengthToRent: 33_000_000 * 4,
                        minSegmentLenInitial: 1, minSegmentLenFinal: 1,
                        maxSegmentLenInitial: 65536 * 1, maxSegmentLenFinal: 32,
                        desiredFillPercentage: .8,
                        youthReturnFactor: .5,
                        updateWindowFillPercentage: 0,
                        updatesPerWindow: 10,
                        defragment: false,
                        fragmentBucketLength: 0,
                        minimumFragmentationPct: 0))
                .ToList());

        // Test random allocation lengths with a large buffer, and combine update windows...
        benchmarks.AddRange(
            GetSuballocators<SomeStruct>(length: 1L << 23, blockLength: 32)
                .Select(suballocator =>
                    Benchmark.Run(
                        imageFolder: _imageFolder,
                        name: suballocator.GetType().Name.Replace("`1", ""),
                        tag: "Random Large - Window Coalesce",
                        suballocator: suballocator,
                        seed: 0,
                        imageWidth: _imageWidth, imageHeight: _imageHeight,
                        totalLengthToRent: 33_000_000 * 4,
                        minSegmentLenInitial: 1, minSegmentLenFinal: 1,
                        maxSegmentLenInitial: 65536 * 1, maxSegmentLenFinal: 32,
                        desiredFillPercentage: .8,
                        youthReturnFactor: .5,
                        updateWindowFillPercentage: .1,
                        updatesPerWindow: 10,
                        defragment: false,
                        fragmentBucketLength: 0,
                        minimumFragmentationPct: 0))
                .ToList());

        // Test random allocation lengths with an even larger buffer, combining windows, and defragmenting...
        benchmarks.AddRange(
            GetSuballocators<SomeStruct>(length: 1L << 23, blockLength: 2)
                .Select(suballocator =>
                    Benchmark.Run(
                        imageFolder: _imageFolder,
                        name: suballocator.GetType().Name.Replace("`1", ""),
                        tag: "Random Larger - Window Coalesce - Defrag",
                        suballocator: suballocator,
                        seed: 0,
                        imageWidth: _imageWidth, imageHeight: _imageHeight,
                        totalLengthToRent: 33_000_000 * 4,
                        minSegmentLenInitial: 1, minSegmentLenFinal: 1,
                        maxSegmentLenInitial: 65536 * 1, maxSegmentLenFinal: 32,
                        desiredFillPercentage: .9,
                        youthReturnFactor: .5,
                        updateWindowFillPercentage: .21,
                        updatesPerWindow: 10,
                        defragment: true,
                        fragmentBucketLength: 65536 * 8,
                        minimumFragmentationPct: .1))
                .ToList());

        // Display results.
        benchmarks.ShowConsole("Results");
        //benchmarks.ShowBarGraph(_imageFolder, $"{tag}.Duration", _imageWidth, _imageHeight, "Name", "Duration (ms)");
        //benchmarks.ShowBarGraph(_imageFolder, $"{tag}.UpdatesLength", _imageWidth, _imageHeight, "Name", "Updates Length (avg)");
        benchmarks.ShowPatternImages();

        Console.ReadKey();
    }

    /// <summary>
    /// Some struct used for testing.
    /// </summary>
    readonly struct SomeStruct
    {
        private readonly long _field1;
        private readonly long _field2;
        private readonly TimeSpan _field3;
        private readonly int _field4;

        public long Field1 { get => _field1; init => _field1 = value; }
        public long Field2 { get => _field2; init => _field2 = value; }
        public TimeSpan Field3 { get => _field3; init => _field3 = value; }
        public int Field4 { get => _field4; init => _field4 = value; }
    }
}
