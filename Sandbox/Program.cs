using System.Diagnostics;
using Suballocation;


/*
var config =
	ManualConfig
	.CreateEmpty() // A configuration for our benchmarks
	.WithOptions(ConfigOptions.DisableOptimizationsValidator)
	.WithOptions(ConfigOptions.JoinSummary)
	.AddLogger(ConsoleLogger.Default)
	.AddJob(Job.Default // Adding second job
		//.WithRuntime(ClrRuntime.Net472) // .NET Framework 4.7.2
		//.WithPlatform(Platform.X64) // Run as x64 application
		//.WithJit(Jit.LegacyJit) // Use LegacyJIT instead of the default RyuJIT
		//.WithGcServer(true) // Use Server GC
		.AsBaseline() // It will be marked as baseline
		.WithWarmupCount(1) // Disable warm-up stage
		
	);

BenchmarkRunner.Run<Testt>(config);
//Console.WriteLine(summary);
Console.ReadKey();

public class Testt
{
	[Benchmark]
	public unsafe void Test1()
	{
		try
		{
			var allocator = new BuddyAllocator<int>(2048, 1024, true);

			for (int i = 0; i < 100; i++)
			{
				var ptr1 = allocator.Rent(1024);
				var ptr2 = allocator.Rent(1024);

				allocator.Return(ptr1);
				allocator.Return(ptr2);
			}
		}
		catch(Exception ex)
		{
			Debugger.Break();
		}
	}

	[Benchmark]
	public unsafe void Test2()
	{
		var allocator = new BuddyAllocator<int>(2048, 1024, true);

		for (int i = 0; i < 100; i++)
		{
			var ptr1 = allocator.Rent(1024);
			var ptr2 = allocator.Rent(1024);

			allocator.Return(ptr1);
			allocator.Return(ptr2);
		}
	}
}*/


void Test3()
{
	var allocator = new LocalityAllocator<int>(65536 * 3, 1024, true);

	try
	{
		Random random = new Random(1);
		Queue<UnmanagedMemorySegment<int>> ptrs = new Queue<UnmanagedMemorySegment<int>>();
		for (ulong i = 0; ; i++)
		{
			if (random.Next(3) >= 1)
				ptrs.Enqueue(allocator.Rent(random.Next(3) == 0 ? 2048 : 800l));
			else if (ptrs.Count > 0)
			{
				var index = random.Next(Math.Min(100, ptrs.Count));
				for (int j = 0; j < index; j++)
					ptrs.Enqueue(ptrs.Dequeue());
				if (ptrs.Count > 0)
					allocator.Return(ptrs.Dequeue());
			}

			allocator.PrintBlocks();
		}
	}
	catch (OutOfMemoryException)
	{
		Console.WriteLine(allocator.Allocations + ", " + allocator.LengthUsed);
	}
}

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
}

Test3();

Console.ReadLine();