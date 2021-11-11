using System.Collections;
using System.Diagnostics;
using Suballocation;

unsafe void Test3()
{
	long blockSize = 1024;
	long bufferLen = 65536 * 3;
	var allocator = new SweepingSuballocator<int>(bufferLen, blockSize);
	BitArray bitArray = new BitArray((int)(allocator.LengthTotal / allocator.BlockLength));

	try
	{
		Random random = new Random(1);
		Queue<NativeMemorySegment<int>> ptrs = new Queue<NativeMemorySegment<int>>();
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
}

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

Test3();

Console.ReadLine();