
//#define TRACE_OUTPUT

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;


//todo: alignment (would just adjust minBlockLength to specified higher size multiple of alignment)
//nint?	

namespace Suballocation
{
	public unsafe sealed class BuddyAllocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
	{
		static long LengthFromLogLength(int logLength) =>
			1l << logLength;

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		readonly struct BlockHeader
		{
			private readonly byte _infoByte;
			private readonly long _prevFree;
			private readonly long _nextFree;

			public bool Occupied { get => (_infoByte & 0b1000_0000) != 0; init => _infoByte = value ? (byte)(_infoByte | 0b1000_0000) : (byte)(_infoByte & 0b0111_1111); }
			public int BlockLengthLog { get => (_infoByte & 0b0111_1111); init => _infoByte = (byte)((_infoByte & 0b1000_0000) | (value & 0b0111_1111)); }
			public long BlockLength
			{
				get => (1l << BlockLengthLog);
				init
				{
					if (BitOperations.IsPow2(value) == false)
					{
						throw new ArgumentOutOfRangeException(nameof(BlockLength), "Must be a power of 2.");
					}

					BlockLengthLog = BitOperations.TrailingZeroCount(value) + 1;
				}
			}

			public long PreviousFree { get => _prevFree; init => _prevFree = value; }
			public long NextFree { get => _nextFree; init => _nextFree = value; }
		}

		public long MinBlockLength;
		private readonly MemoryHandle _memoryHandle;
		private readonly bool _privatelyOwned;
		private T* _pElems;
		private BlockHeader* _pIndex;
        private long _maxBlockLength;
        private long _indexLength;
		private long[] _freeBlockIndexesStart;
		private int _blockLengths;
		private bool _disposed;

		public BuddyAllocator(long length, long minBlockLength = 1, bool zeroed = false)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size 0.");
			if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");

			LengthTotal = length;
			_privatelyOwned = true;
			if (zeroed)
			{
				_pElems = (T*)NativeMemory.AllocZeroed((nuint)length, (nuint)Unsafe.SizeOf<T>());
			}
			else
			{
				_pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
			}

			Init(minBlockLength);
		}

		public BuddyAllocator(T* pData, long length, long minBlockLength = 1)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a BuddyAllocator of size 0.");
			if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");
			if (pData == null) throw new ArgumentNullException(nameof(pData));

			LengthTotal = length;
			_pElems = pData;

			Init(minBlockLength);
		}

		public BuddyAllocator(Memory<T> data, long minBlockLength = 1)
		{
			if (data.Length == 0) throw new ArgumentOutOfRangeException(nameof(data), $"Cannot allocate a BuddyAllocator of size 0.");
			if (minBlockLength > (uint)data.Length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(data.Length)}.");

			LengthTotal = (long)data.Length;
			_memoryHandle = data.Pin();
			_pElems = (T*)_memoryHandle.Pointer;

			Init(minBlockLength);
		}

		private void Init(long minBlockLength)
		{
			MinBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)minBlockLength);

			_indexLength = LengthTotal >> BitOperations.TrailingZeroCount(MinBlockLength);
			_pIndex = (BlockHeader*)NativeMemory.AllocZeroed((nuint)(_indexLength * (long)sizeof(BlockHeader)));
			_maxBlockLength = (long)BitOperations.RoundUpToPowerOf2((ulong)_indexLength);
			var maxBlockShift = BitOperations.TrailingZeroCount(_maxBlockLength);
			_blockLengths = maxBlockShift + 1;
			_freeBlockIndexesStart = new long[_blockLengths];
			InitBlocks();
		}

		private void InitBlocks()
		{
			_freeBlockIndexesStart.AsSpan().Fill(long.MaxValue);

			long index = 0;
			for (int i = 0; i < _freeBlockIndexesStart.Length; i++)
			{
				var blockLength = _maxBlockLength >> i;

				if (blockLength > _indexLength - index)
				{
					continue;
				}

				var blockLengthLog = BitOperations.TrailingZeroCount(blockLength);

				ref BlockHeader header = ref _pIndex[index];

				header = header with { Occupied = false, BlockLengthLog = blockLengthLog, NextFree = long.MaxValue, PreviousFree = long.MaxValue };

				_freeBlockIndexesStart[_freeBlockIndexesStart.Length - i - 1] = index;

				index += header.BlockLength;
			}
		}

		public long BlocksUsed { get; private set; }

		public long SizeUsed => LengthUsed * (long)Unsafe.SizeOf<T>();

		public long SizeTotal => LengthTotal * (long)Unsafe.SizeOf<T>();

		public long Allocations { get; private set; }

		public long LengthUsed { get => BlocksUsed * MinBlockLength; }

		public long LengthTotal { get; init; }

		public T* PElems => _pElems;



		public NativeMemorySegmentResource<T> RentResource(long length = 1)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

			var rawSegment = Alloc(length);

			return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index * MinBlockLength, rawSegment.Length);
		}

		public void ReturnResource(NativeMemorySegmentResource<T> segment)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));

			Free(segment.PElems - _pElems, segment.Length);
		}

		public NativeMemorySegment<T> Rent(long length = 1)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

			var rawSegment = Alloc(length);

			return new NativeMemorySegment<T>(_pElems + rawSegment.Index * MinBlockLength, rawSegment.Length);
		}

		public void Return(NativeMemorySegment<T> segment)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));

			Free(segment.PElems - _pElems, segment.Length);
		}







		private unsafe (long Index, long Length) Alloc(long length)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");
			if (_disposed) throw new ObjectDisposedException(nameof(BuddyAllocator<T>));

			long index = -1;
			long actualLength = -1;
			int blockLengthIndex = BitOperations.Log2(BitOperations.RoundUpToPowerOf2((ulong)length));// (int)(BitOperations.RoundUpToPowerOf2((ulong)length) >> (_blockShift + 1));

#if TRACE_OUTPUT
			Console.WriteLine($"Rent length desired: {length:N0}");
#endif

			for (int i = blockLengthIndex; i < _freeBlockIndexesStart.Length; i++)
			{
				long freeBlockIndexIndex = _freeBlockIndexesStart[i];

				//todo: could do this check in constant time with bits
				if (freeBlockIndexIndex == long.MaxValue)
				{
					// no free blocks of idea size; try a larger size
#if TRACE_OUTPUT
					Console.WriteLine($"Free block of length {1ul << i} not found.");
#endif
					continue;
				}

				while(i > blockLengthIndex)
				{
					// Split in half

					ref BlockHeader header1 = ref _pIndex[freeBlockIndexIndex];

					Debug.Assert(header1.Occupied == false);

					RemoveFromFreeList(ref header1);

					header1 = header1 with { BlockLengthLog = header1.BlockLengthLog - 1, NextFree = freeBlockIndexIndex + (1l << (header1.BlockLengthLog - 1)), PreviousFree = long.MaxValue };

					ref BlockHeader header2 = ref _pIndex[header1.NextFree];
					header2 = header2 with { BlockLengthLog = header1.BlockLengthLog, NextFree = _freeBlockIndexesStart[header1.BlockLengthLog], PreviousFree = freeBlockIndexIndex };

					_freeBlockIndexesStart[header1.BlockLengthLog] = freeBlockIndexIndex;

					if (header2.NextFree != long.MaxValue)
					{
						ref BlockHeader nextHeader = ref _pIndex[header2.NextFree];
						nextHeader = nextHeader with { PreviousFree = header1.NextFree };
					}

					Debug.Assert(header1.Occupied == false);
					Debug.Assert(header2.Occupied == false);

#if TRACE_OUTPUT
					Console.WriteLine($"Free blocks split of length {header1.BlockLength} at index {freeBlockIndexIndex} and {header1.NextFree}.");
#endif
					i--;
				}

#if TRACE_OUTPUT
				Console.WriteLine($"Free block of length {1ul << i} found at index {freeBlockIndexIndex}.");
#endif

				ref BlockHeader header = ref _pIndex[freeBlockIndexIndex];

				Debug.Assert(header.Occupied == false);

				RemoveFromFreeList(ref header);

				header = header with { Occupied = true, BlockLengthLog = blockLengthIndex };

				index = freeBlockIndexIndex;

				break;
			}

			if (index == -1)
			{
				throw new OutOfMemoryException();
			}

			Allocations++;
			BlocksUsed += (long)BitOperations.RoundUpToPowerOf2((ulong)length) / MinBlockLength;

			return (index, length);
		}

		private unsafe void Free(long offset, long length)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(BuddyAllocator<T>));

			long freeBlockIndexIndex = offset / MinBlockLength;

			if (freeBlockIndexIndex < 0 || freeBlockIndexIndex >= _indexLength) 
				throw new ArgumentNullException(nameof(offset));

			ref BlockHeader header = ref _pIndex[freeBlockIndexIndex];

			if (header.Occupied == false)
				throw new ArgumentException($"No rented segment at offset {offset} found.");

			header = header with { Occupied = false };

			Allocations--;
			BlocksUsed -= (long)BitOperations.RoundUpToPowerOf2((ulong)header.BlockLength);

#if TRACE_OUTPUT
			Console.WriteLine($"Returning block of length {header.BlockLength} at index {freeBlockIndexIndex}.");
#endif

			void Combine(long blockIndexIndex, int lengthLog)
			{
				ref BlockHeader header = ref _pIndex[blockIndexIndex];

				var buddyBlockIndexIndex = blockIndexIndex ^ (1l << lengthLog);

				if (buddyBlockIndexIndex >= _indexLength)
				{
					// No buddy; at the end of the buffer.
					var nextFree = _freeBlockIndexesStart[lengthLog] == blockIndexIndex ? long.MaxValue : _freeBlockIndexesStart[lengthLog];
					header = header with { Occupied = false, BlockLengthLog = lengthLog, NextFree = nextFree, PreviousFree = long.MaxValue };
					_freeBlockIndexesStart[lengthLog] = blockIndexIndex;

					if (nextFree != long.MaxValue)
					{
						ref BlockHeader nextHeader = ref _pIndex[header.NextFree];
						nextHeader = nextHeader with { PreviousFree = blockIndexIndex };
					}

					return;
				}

				ref BlockHeader buddyHeader = ref _pIndex[buddyBlockIndexIndex];

				if (buddyHeader.Occupied == true)
				{
					// No free buddy
					var nextFree = _freeBlockIndexesStart[lengthLog] == blockIndexIndex ? long.MaxValue : _freeBlockIndexesStart[lengthLog];
					header = header with { Occupied = false, BlockLengthLog = lengthLog, NextFree = nextFree, PreviousFree = long.MaxValue };
					_freeBlockIndexesStart[lengthLog] = blockIndexIndex;

					if (nextFree != long.MaxValue)
					{
						ref BlockHeader nextHeader = ref _pIndex[header.NextFree];
						nextHeader = nextHeader with { PreviousFree = blockIndexIndex };
					}

					return;
				}

#if TRACE_OUTPUT
				Console.WriteLine($"Combined blocks of length {1ul << lengthLog} at index {blockIndexIndex} and {buddyBlockIndexIndex}.");
#endif

				if (buddyBlockIndexIndex < blockIndexIndex)
				{
					blockIndexIndex = buddyBlockIndexIndex;
				}

				RemoveFromFreeList(ref buddyHeader);

				Combine(blockIndexIndex, lengthLog + 1);
			}

			Combine(freeBlockIndexIndex, header.BlockLengthLog);
		}

		private void RemoveFromFreeList(ref BlockHeader header)
		{
			if (header.NextFree != long.MaxValue)
			{
				ref BlockHeader nextHeader = ref _pIndex[header.NextFree];

				nextHeader = nextHeader with { PreviousFree = header.PreviousFree };

				Debug.Assert(nextHeader.Occupied == false);

				if (header.PreviousFree != long.MaxValue)
				{
					ref BlockHeader prevHeader = ref _pIndex[header.PreviousFree];

					prevHeader = prevHeader with { NextFree = header.NextFree };
				}
				else
				{
					_freeBlockIndexesStart[header.BlockLengthLog] = header.NextFree;
				}
			}
			else if (header.PreviousFree != long.MaxValue)
			{
				ref BlockHeader prevHeader = ref _pIndex[header.PreviousFree];

				prevHeader = prevHeader with { NextFree = long.MaxValue };
			}
			else
			{
				_freeBlockIndexesStart[header.BlockLengthLog] = long.MaxValue;
			}
		}

		public void Clear()
		{
			Allocations = 0;
			BlocksUsed = 0;

			for (long i = 0; i < _indexLength; i += uint.MaxValue / Unsafe.SizeOf<BlockHeader>())
			{
				uint length = (uint)Math.Min(uint.MaxValue / Unsafe.SizeOf<BlockHeader>(), _indexLength - i);

				Unsafe.InitBlock(_pIndex + i, 0, length * (uint)Unsafe.SizeOf<BlockHeader>());
			}

			InitBlocks();
		}




		public static long GetLengthRequiredToPreventDefragmentation(long maxLength, long maxBlockSize)
		{
			// Defoe, Delvin C., "Storage Coalescing" Report Number: WUCSE-2003-69 (2003). All Computer Science and Engineering Research.
			// https://openscholarship.wustl.edu/cse_research/1115 
			// M(log n + 2)/2
			return (maxLength * ((long)Math.Log(maxBlockSize) + 2)) >> 1;
		}



		private void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (disposing)
				{
					// TODO: dispose managed state (managed objects)
				}

				// TODO: free unmanaged resources (unmanaged objects) and override finalizer
				// TODO: set large fields to null

				NativeMemory.Free(_pIndex);

				_memoryHandle.Dispose(); //todo: if default, what will happen?

				if (_privatelyOwned)
				{
					NativeMemory.Free(_pElems);
				}

				_disposed = true;
			}
		}

		~BuddyAllocator()
		{
			Dispose(disposing: false);
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
