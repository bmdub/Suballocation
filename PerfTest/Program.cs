using Suballocation;
using Suballocation.Suballocators;

namespace PerfTest;

//todo:
// Sampling strategy. 
// OptimizeHead()
// Configurable head reset strategy
// Configurable direction strategy with default.

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
            //yield return new new ArrayPoolSuballocator<SomeStruct>(length);
            //yield return new new MemoryPoolSuballocator<SomeStruct>(length);
        }

        // Run a test for each suballocator.
        {
            string tag = "Random Short";

            var results =
                GetSuballocators<SomeStruct>(length: 2048, blockLength: 2)
                    .Select(suballocator =>
                        Benchmark.Run(
                            imageFolder: _imageFolder,
                            name: suballocator.GetType().Name.Replace("`1", ""),
                            tag: tag,
                            suballocator: suballocator,
                            seed: 0,
                            imageWidth: _imageWidth, imageHeight: _imageHeight,
                            totalLengthToRent: 64_000,
                            minSegmentLenInitial: 1, minSegmentLenFinal: 1,
                            maxSegmentLenInitial: 32 * 1, maxSegmentLenFinal: 2,
                            desiredFillPercentage: .9,
                            youthReturnFactor: .5,
                            updateWindowFillPercentage: .21,
                            updatesPerWindow: 1,
                            defragment: true,
                            fragmentBucketLength: 128))
                    .ToList();

            // Display results.
            results.ShowConsole(tag);
            results.ShowBarGraph(_imageFolder, $"{tag}.Duration", _imageWidth, _imageHeight, "Name", "Duration (ms)");
            results.ShowPatternImages();

            Console.ReadKey();
        }

        // Run a test for each suballocator.
        {
            string tag = "Random";

            var results =
                GetSuballocators<SomeStruct>(length: 1L << 23, blockLength: 2)
                    .Select(suballocator =>
                        Benchmark.Run(
                            imageFolder: _imageFolder,
                            name: suballocator.GetType().Name.Replace("`1", ""),
                            tag: tag,
                            suballocator: suballocator,
                            seed: 0,
                            imageWidth: _imageWidth, imageHeight: _imageHeight,
                            totalLengthToRent: 33_000_000 * 4,
                            minSegmentLenInitial: 1, minSegmentLenFinal: 1,
                            maxSegmentLenInitial: 65536 * 1, maxSegmentLenFinal: 32,
                            desiredFillPercentage: .6,
                            youthReturnFactor: .5,
                            updateWindowFillPercentage: .21,
                            updatesPerWindow: 10,
                            defragment: false,
                            fragmentBucketLength: 65536))
                    .ToList();

            // Display results.
            results.ShowConsole(tag);
            results.ShowBarGraph(_imageFolder, $"{tag}.Duration", _imageWidth, _imageHeight, "Name", "Duration (ms)");
            results.ShowPatternImages();

            Console.ReadKey();
        }
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
