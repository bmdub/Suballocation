using System.Collections;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Suballocation;


public class Program
{
    static void Main(string[] args)
    {

        var config =
            ManualConfig
            .CreateEmpty() // A configuration for our benchmarks
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            //.WithOptions(ConfigOptions.JoinSummary)
            .AddLogger(ConsoleLogger.Default)
            .AddExporter(RPlotExporter.Default)
            //.AddColumn(new GenericColumn("Method", cs => cs.Descriptor.WorkloadMethod.Name))
            //.AddColumn(new GenericColumn("Parameter", cs => cs.Job.))
            //.AddColumn(new GenericColumn("idk", cs => cs.Job.DisplayInfo))
            //.AddColumn(StatisticColumn.Min)
            //.AddColumn(StatisticColumn.Max)
            //.AddColumn(StatisticColumn.Mean)
            //.AddColumn(StatisticColumn.Median)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared))
            //.AddColumnProvider(new ParamsColumnProvider())
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddJob(Job.Default // Adding second job
                                //.WithRuntime(ClrRuntime.Net472) // .NET Framework 4.7.2
                                //.WithPlatform(Platform.X64) // Run as x64 application
                                //.WithJit(Jit.LegacyJit) // Use LegacyJIT instead of the default RyuJIT
                                //.WithGcServer(true) // Use Server GC
                                //.AsBaseline() // It will be marked as baseline
                                //.WithWarmupCount(0) // Disable warm-up stage
                .WithUnrollFactor(1)
                .WithInvocationCount(1)
                .WithIterationCount(1)
                .WithWarmupCount(0)
            );

        BenchmarkRunner.Run<Benchmarkz>(config);
        //Console.WriteLine(summary);
        Console.ReadKey();
    }


    public struct SuballocatorWrapper<T> where T : unmanaged
    {
        public ISuballocator<T> Suballocator { get; set; }

        public override string ToString() => Suballocator.GetType().Name;
    }


    //[SimpleJob(RunStrategy.ColdStart, targetCount: 5)]
    [DryJob]
    public class Benchmarkz
    {
        [ParamsSource(nameof(GetAllocators))]
        public SuballocatorWrapper<int> _allocator;

        public IEnumerable<SuballocatorWrapper<int>> GetAllocators()
        {
            yield return new SuballocatorWrapper<int>() { Suballocator = new StackSuballocator<int>(1) };
            yield return new SuballocatorWrapper<int>() { Suballocator = new SweepingSuballocator<int>(1, 1) };
            //yield return new BuddyAllocator<int>(2048, 1024, true);
        }

        public Benchmarkz()
        {
        }

        [GlobalSetup]
        public void Startup()
        {
        }

        [Benchmark]
        public unsafe void SequentialFillFixed()
        {
            for (int i = 0; i < _allocator.Suballocator.LengthTotal; i++)
            {
                _allocator.Suballocator.Rent(1);
            }
        }

        [Benchmark]
        public unsafe void SequentialFillReturnFixed()
        {
            List<UnmanagedMemorySegment<int>> segments = new List<UnmanagedMemorySegment<int>>((int)_allocator.Suballocator.LengthTotal);

            for (int i = 0; i < _allocator.Suballocator.LengthTotal; i++)
            {
                segments.Add(_allocator.Suballocator.Rent(1));
            }

            foreach (var segment in segments)
            {
                _allocator.Suballocator.Return(segment);
            }
        }
        
        [IterationCleanup]
        public void Cleanup()
        {
            _allocator.Suballocator.Clear();
        }

        [GlobalCleanup]
        public void Cleanup2()
        {
            _allocator.Suballocator.Dispose();
        }
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

    public class GenericColumn : IColumn
    {
        private readonly Func<BenchmarkCase, string> getTag;

        public string Id { get; }
        public string ColumnName { get; }

        public GenericColumn(string columnName, Func<BenchmarkCase, string> getTag)
        {
            this.getTag = getTag;
            ColumnName = columnName;
            Id = "a" + nameof(GenericColumn) + "." + ColumnName;
        }

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
            getTag(benchmarkCase);

        public bool IsAvailable(Summary summary) => true;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Statistics; //
        public int PriorityInCategory => 0;
        public bool IsNumeric => false;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => $"Custom '{ColumnName}' tag column";
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public override string ToString() => ColumnName;
    }
}
