using System.Collections;
using System.Diagnostics;
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

//todo:
// change fixed stack allocator to variable one
//  remove other stack allocator
//  try block-based seq allocator in its place (then only need bit array). block headers?
// sequential allocator faster with hash map? need to make a native one.
// compaction
// also option for full compaction?
// consider segment updates, which will affect the update window. combine with compaction?
// will starting capacities make better efficiency?

// local alloc: use queues for some work, heap for returning


// tests
// random free/rent for fixed level allocations
// varying block sizes (for varying alloc tests)
// actual segment usage / copy
// compaction vs without
// overprovisioning needed

// stats
// failed with OOO count
// compaction rate
// update windows / locality of rentals
// free space when OOO
// amt data compacted
// other mem usage

// b-heap?

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
        long length = 1L << 26;

        Test<SomeStruct>(1, length, 1024, 65536 / 2, 16);// (int)(length / 10000), 2);

        Console.ReadKey();
    }

    static void Test<T>(int iterations, long length, int minSegLen, int maxSegLen, long blockLength) where T : unmanaged
    {
        Console.WriteLine($"Buffer Length: {length}");

		List<BenchmarkResult> results;

		results = new List<BenchmarkResult>()
        {
            new FillFixedBenchmark<T>(new FixedStackSuballocator<T>(length)).Run(iterations),
            new FillFixedBenchmark<T>(new StackSuballocator<T>(length)).Run(iterations),
            new FillFixedBenchmark<T>(new SequentialFitSuballocator<T>(length)).Run(iterations),
            new FillFixedBenchmark<T>(new BuddySuballocator<T>(length, 1)).Run(iterations),
            new FillFixedBenchmark<T>(new LocalBuddySuballocator<T>(length, 1)).Run(iterations),
			//new SequentialFillFixedBenchmark<T>(new ArrayPoolSuballocator<T>(length)).Run(iterations),
			//new SequentialFillFixedBenchmark<T>(new MemoryPoolSuballocator<T>(length)).Run(iterations),
		};

        results.WriteToConsole();
		results.WriteToBarGraph(nameof(FillFixedBenchmark<T>), "Allocator", "Duration (ms)", result => result.GetValue("Allocator"), result => double.Parse(result.GetValue("DurationMs")));

        results = new List<BenchmarkResult>()
        {
            new FillEmptyFixedBenchmark<T>(new FixedStackSuballocator<T>(length)).Run(iterations),
            new FillEmptyFixedBenchmark<T>(new StackSuballocator<T>(length)).Run(iterations),
            new FillEmptyFixedBenchmark<T>(new SequentialFitSuballocator<T>(length)).Run(iterations),
            new FillEmptyFixedBenchmark<T>(new BuddySuballocator<T>(length, 1)).Run(iterations),
            new FillEmptyFixedBenchmark<T>(new LocalBuddySuballocator<T>(length, 1)).Run(iterations),
			//new SequentialFillReturnFixedBenchmark<T>(new ArrayPoolSuballocator<T>(length)).Run(iterations),
			//new SequentialFillReturnFixedBenchmark<T>(new MemoryPoolSuballocator<T>(length)).Run(iterations),
		};

        results.WriteToConsole();
		results.WriteToBarGraph(nameof(FillEmptyFixedBenchmark<T>), "Allocator", "Duration (ms)", result => result.GetValue("Allocator"), result => double.Parse(result.GetValue("DurationMs")));
		
		results = new List<BenchmarkResult>()
		{
			new FillVariableBenchmark<T>(new FixedStackSuballocator<T>(length), 0, maxSegLen).Run(iterations),
			new FillVariableBenchmark<T>(new SequentialFitSuballocator<T>(length), 0, maxSegLen).Run(iterations),
            new FillVariableBenchmark<T>(new BuddySuballocator<T>(length, blockLength), 0, maxSegLen).Run(iterations),
            new FillVariableBenchmark<T>(new LocalBuddySuballocator<T>(length, blockLength), 0, maxSegLen).Run(iterations),
			//new SequentialFillVariableBenchmark<T>(new ArrayPoolSuballocator<T>(length), 0, maxSegLen).Run(iterations),
			//new SequentialFillVariableBenchmark<T>(new MemoryPoolSuballocator<T>(length), 0, maxSegLen).Run(iterations),
		};

        results.WriteToConsole();
		results.WriteToBarGraph(nameof(FillVariableBenchmark<T>), "Allocator", "Duration (ms)", result => result.GetValue("Allocator"), result => double.Parse(result.GetValue("DurationMs")));
		
		results = new List<BenchmarkResult>()
		{
			new RandomBenchmark<T>(new SequentialFitSuballocator<T>(length), 0, minSegLen, maxSegLen).Run(iterations),
            new RandomBenchmark<T>(new BuddySuballocator<T>(length, blockLength), 0, minSegLen, maxSegLen).Run(iterations),
            new RandomBenchmark<T>(new LocalBuddySuballocator<T>(length, blockLength), 0, minSegLen, maxSegLen).Run(iterations),
			//new SequentialFillVariableBenchmark<T>(new ArrayPoolSuballocator<T>(length), 0, maxSegLen).Run(iterations),
			//new SequentialFillVariableBenchmark<T>(new MemoryPoolSuballocator<T>(length), 0, maxSegLen).Run(iterations),
		};

        results.WriteToConsole();
		results.WriteToBarGraph(nameof(RandomBenchmark<T>), "Allocator", "Duration (ms)", result => result.GetValue("Allocator"), result => double.Parse(result.GetValue("DurationMs")));


		//results.GroupBy(result => result.GetValue("Allocator")).WriteToConsole();

		//results.WriteToGroupedBarGraph();
	}

    /*
	unsafe void Test3()
	{
		long blockSize = 1024;
		long bufferLen = 65536 * 3;
		var allocator = new SweepingSuballocator<int>(bufferLen, blockSize);
		BitArray bitArray = new BitArray((int)(allocator.LengthTotal / allocator.BlockLength));

		try
		{
			Random random = new Random(1);
			Queue<UnmanagedMemorySegment<int>> ptrs = new Queue<UnmanagedMemorySegment<int>>();
			for (ulong i = 0; ; i++)
			{
				var len = random.Next(3) == 0 ? 2048 : 800L;
				if (random.Next(3) >= 1)
				{
					var seg = allocator.Rent(len);
					ptrs.Enqueue(seg);

					for (long j = 0; j < seg.Length / allocator.BlockLength; j++)
						bitArray[(int)(((long)(seg.PElems - allocator.PElems)) / allocator.BlockLength)] = true;
				}
				else if (ptrs.Count > 0)
				{
					var index = random.Next(Math.Min(100, ptrs.Count));
					for (int j = 0; j < index; j++)
						ptrs.Enqueue(ptrs.Dequeue());
					if (ptrs.Count > 0)
					{
						var seg = ptrs.Dequeue();
						allocator.Return(seg);

						for (long j = 0; j < seg.Length / allocator.BlockLength; j++)
							bitArray[(int)(((long)(seg.PElems - allocator.PElems)) / allocator.BlockLength)] = false;
					}
				}

				for (int j = 0; j < bitArray.Length; j++)
					Console.Write(bitArray[j] ? 'X' : '_');
				Console.WriteLine();
			}
		}
		catch (OutOfMemoryException)
		{
			Console.WriteLine(allocator.Allocations + ", " + allocator.LengthUsed);
		}
	}*/

    /*
	void Test2()
	{
		var allocator = new BuddyAllocator<int>(65536, 1024, true);
		ulong count = allocator.Length / allocator.MinBlockLength;

		List<ulong> ptrs = new List<ulong>();
		for (ulong i = 0; i < count; i++)
		{
			ptrs.Add((ulong)allocator.Rent(800));
			if (i % 10 == 0)
				allocator.PrintBlocks();
		}

		for (ulong i = 0; i < count; i++)
		{
			allocator.Return((int*)ptrs[(int)i]);
			if (i % 10 == 0)
				allocator.PrintBlocks();
		}

		allocator.PrintBlocks();
	}

	unsafe void Test()
	{
		var allocator = new BuddyAllocator<int>(4096, 1024, true);

		for (int i = 0; i < 100; i++)
		{
			var ptr1 = allocator.Rent(1024);
			var ptr2 = allocator.Rent(1024);
			var ptr3 = allocator.Rent(1024);
			var ptr4 = allocator.Rent(53);

			allocator.PrintBlocks();

			allocator.Return(ptr1);
			allocator.Return(ptr2);
			allocator.Return(ptr3);
			allocator.Return(ptr4);
		}
	}*/
}
