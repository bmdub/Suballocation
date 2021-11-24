using System.Collections;
using System.Diagnostics;
using SkiaSharp;
using Suballocation;

namespace PerfTest;

//https://docs.microsoft.com/en-us/archive/msdn-magazine/2000/december/garbage-collection-part-2-automatic-memory-management-in-the-microsoft-net-framework 
// The newer an object is, the shorter its lifetime will be.
// The older an object is, the longer its lifetime will be.
// Newer objects tend to have strong relationships to each other and are frequently accessed around the same time.
// Compacting a portion of the heap is faster than compacting the whole heap.

//me
// The larger an object is, the longer its lifetime will be.
// The larger an object is, the larger the acceptable update window / placement distance.
// Less fragmentation if segment is near similar-sized segments?
// Larger objects near center will hurt locality
// Objects near edges may be re-traversed more quickly (and skipped over).

//todo:
// Sampling strategy. 
// OptimizeHead()
// Configurable head reset strategy
// Configurable direction strategy with default.
// delete sequential fit?
// compaction
// also option for full compaction?
// consider segment updates, which will affect the update window. combine with compaction?
// will starting capacities make better efficiency?
// try not using reaodnly struct
// alternate heap traversals and modifications? batch puts? peek over dequeue.

// tests
// random free/rent for fixed level allocations
// actual segment usage / copy
// compaction vs without
// overprovisioning needed

// stats
// failed with OOO count
// compaction rate
// update windows / locality of rentals
// free space when finished
// amt data compacted
// other mem usage

public partial class Program
{
    struct SomeStruct
    {
        long asdf;
        long qwer;
        TimeSpan TimeSpan;
        int a;
    }

    static void Main(string[] args)
    {
		long length = 1L << 21;
		int minSegLen = 1;
		int maxSegLen = 65536 / 10;
		long blockLength = 1;

		TestRandom(minSegLen, maxSegLen, 
			new SequentialFitSuballocator<SomeStruct>(length),
			new SequentialBlockSuballocator<SomeStruct>(length, blockLength),
			new BuddySuballocator<SomeStruct>(length, blockLength),
			new DirectionalBlockSuballocator<SomeStruct>(length, blockLength)
			//new ArrayPoolSuballocator<SomeStruct>(length),
			//new MemoryPoolSuballocator<SomeStruct>(length)
			);


		Console.ReadKey();
    }

	static void TestRandom<T>(int minSegLen, int maxSegLen, params ISuballocator<T>[] suballocators) where T : unmanaged
	{
		var updateWindowTracker = new UpdateWindowTracker1<T>(.1);// Math.BitDecrement(1.0));

		var results = new List<BenchmarkResult>();

		foreach(var suballocator in suballocators)
        {
			results.Add(
			new Benchmark<T>(suballocator, 0, 1024, 1024)
				//.Run(.50f, minSegLen, maxSegLen, updateWindowTracker, 10000)
				//.Run(100.0f, maxSegLen, minSegLen, maxSegLen, minSegLen, .75, .5, updateWindowTracker, 2000)
				.Run(100.0f, minSegLen, minSegLen, maxSegLen, maxSegLen, .15, .5, updateWindowTracker, 200)
				);
        }

		results.WriteToConsole();
		results.WriteToBarGraph("Random", "Allocator", "Duration (ms)", result => result.GetValue("Allocator"), result => double.Parse(result.GetValue("DurationMs")));
		results.ShowImages();

		//results.GroupBy(result => result.GetValue("Allocator")).WriteToConsole();

		//results.WriteToGroupedBarGraph();
	}
}
