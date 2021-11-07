
#define TRACE_OUTPUT

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
	public unsafe sealed class BuddyAllocator<T> : IDisposable where T : unmanaged
	{
		public static readonly int ElementSize;

		static BuddyAllocator()
		{
			ElementSize = Unsafe.SizeOf<T>();
		}

		static ulong LengthFromLogLength(int logLength) =>
			1ul << logLength;

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		readonly struct BlockHeader
		{
			private readonly byte _infoByte;
			private readonly ulong _prevFree;
			private readonly ulong _nextFree;

			public bool Occupied { get => (_infoByte & 0b1000_0000) != 0; init => _infoByte = value ? (byte)(_infoByte | 0b1000_0000) : (byte)(_infoByte & 0b0111_1111); }
			public int BlockLengthLog { get => (_infoByte & 0b0111_1111); init => _infoByte = (byte)((_infoByte & 0b1000_0000) | (value & 0b0111_1111)); }
			public ulong BlockLength
			{
				get => (1ul << BlockLengthLog);
				init
				{
					if (BitOperations.IsPow2(value) == false)
					{
						throw new ArgumentOutOfRangeException(nameof(BlockLength), "Must be a power of 2.");
					}

					BlockLengthLog = BitOperations.TrailingZeroCount(value) + 1;
				}
			}

			public ulong PreviousFree { get => _prevFree; init => _prevFree = value; }
			public ulong NextFree { get => _nextFree; init => _nextFree = value; }
		}

		public ulong Length { get; init; }
		public ulong Size => Length * (ulong)ElementSize;
		public ulong SizeUsed => LengthUsed * (ulong)ElementSize;
		public ulong LengthUsed => BlocksUsed * MinBlockLength;
		public ulong BlocksUsed { get; private set; }
		public ulong Allocations { get; private set; }
		public readonly ulong MinBlockLength;
		private readonly int _blockShift;
		private readonly T* _pData;
		private readonly MemoryHandle _memoryHandle;
		private readonly bool _privatelyOwned;
		private readonly BlockHeader* _pIndex;
		private readonly ulong _indexLength;
		private readonly ulong[] _freeBlockIndexesStart;
		private readonly int _blockLengths;
		private bool _disposed;

		public BuddyAllocator(ulong length, ulong minBlockLength = 1, bool zeroed = false)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size 0.");
			if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");

			Length = length;
			_privatelyOwned = true;
			if (zeroed)
			{
				_pData = (T*)NativeMemory.AllocZeroed((nuint)length, (nuint)ElementSize);
			}
			else
			{
				_pData = (T*)NativeMemory.Alloc((nuint)length, (nuint)ElementSize);
			}
			MinBlockLength = BitOperations.RoundUpToPowerOf2(minBlockLength);
			_blockShift = BitOperations.TrailingZeroCount(MinBlockLength);

			_indexLength = length >> _blockShift; //?
			_pIndex = (BlockHeader*)NativeMemory.AllocZeroed((nuint)(_indexLength * (ulong)sizeof(BlockHeader)));
			var maxBlockLength = BitOperations.RoundUpToPowerOf2(_indexLength);
			var maxBlockShift = BitOperations.TrailingZeroCount(maxBlockLength);
			_blockLengths = maxBlockShift + 1;
			_freeBlockIndexesStart = new ulong[_blockLengths];
			_freeBlockIndexesStart.AsSpan().Fill(ulong.MaxValue);

			ulong index = 0;
			for (int i = 0; i < _freeBlockIndexesStart.Length; i++)
			{
				var blockLength = maxBlockLength >> i;

				if (blockLength > _indexLength - index)
				{
					continue;
				}

				var blockLengthLog = BitOperations.TrailingZeroCount(blockLength);

				ref BlockHeader header = ref _pIndex[index];

				header = header with { Occupied = false, BlockLengthLog = blockLengthLog, NextFree = ulong.MaxValue, PreviousFree = ulong.MaxValue };

				_freeBlockIndexesStart[_freeBlockIndexesStart.Length - i - 1] = index;

				index += header.BlockLength;
			}

#if TRACE_OUTPUT
			Console.WriteLine($"Length: {Length:N0}");
			Console.WriteLine($"MinBlockLength desired: {minBlockLength:N0}");
			Console.WriteLine($"MinBlockLength actual: {MinBlockLength:N0}");
			Console.WriteLine($"Index length: {_indexLength:N0}");
			Console.WriteLine($"Block sizes: {_blockLengths:N0}");
			for (int i = 0; i < _freeBlockIndexesStart.Length; i++)
				Console.WriteLine($"Free index start [{i}]: {_freeBlockIndexesStart[i]:N0}");
#endif
		}

		public BuddyAllocator(T* pData, ulong length, ulong minBlockLength = 1)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a BuddyAllocator of size 0.");
			if (minBlockLength > length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(length)}.");
			if (pData == null) throw new ArgumentNullException(nameof(pData));

			Length = length;
			_pData = pData;
			MinBlockLength = BitOperations.RoundUpToPowerOf2(minBlockLength);

		}

		public BuddyAllocator(Memory<T> data, ulong minBlockLength = 1)
		{
			if (data.Length == 0) throw new ArgumentOutOfRangeException(nameof(data), $"Cannot allocate a BuddyAllocator of size 0.");
			if (minBlockLength > (uint)data.Length) throw new ArgumentOutOfRangeException(nameof(minBlockLength), $"Cannot have a block size that's larger than {nameof(data.Length)}.");

			Length = (ulong)data.Length;
			_memoryHandle = data.Pin();
			_pData = (T*)_memoryHandle.Pointer;
			MinBlockLength = BitOperations.RoundUpToPowerOf2(minBlockLength);

		}

		public unsafe T* Rent(ulong length = 1)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");
			if (_disposed) throw new ObjectDisposedException(nameof(BuddyAllocator<T>));

			T* segmentPtr = null;
			int blockLengthIndex = (int)(BitOperations.RoundUpToPowerOf2(length) >> (_blockShift + 1));

#if TRACE_OUTPUT
			Console.WriteLine($"Rent length desired: {length:N0}");
#endif

			for (int i = blockLengthIndex; i < _freeBlockIndexesStart.Length; i++)
			{
				ulong freeBlockIndexIndex = _freeBlockIndexesStart[i];

				//todo: could do this check in constant time with bits
				if (freeBlockIndexIndex == ulong.MaxValue)
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

					header1 = header1 with { BlockLengthLog = header1.BlockLengthLog - 1, NextFree = freeBlockIndexIndex + (1ul << (header1.BlockLengthLog - 1)), PreviousFree = ulong.MaxValue };

					ref BlockHeader header2 = ref _pIndex[header1.NextFree];
					header2 = header2 with { BlockLengthLog = header1.BlockLengthLog, NextFree = _freeBlockIndexesStart[header1.BlockLengthLog], PreviousFree = freeBlockIndexIndex };

					_freeBlockIndexesStart[header1.BlockLengthLog] = freeBlockIndexIndex;

					if (header2.NextFree != ulong.MaxValue)
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

				segmentPtr = _pData + freeBlockIndexIndex * MinBlockLength;

				break;
			}

			if (segmentPtr == null)
			{
				throw new OutOfMemoryException();
			}

			Allocations++;
			BlocksUsed += BitOperations.RoundUpToPowerOf2(length) / MinBlockLength;

			return segmentPtr;
		}

		public unsafe void Return(T* pSegment)
		{
			if (pSegment == null) throw new ArgumentNullException(nameof(pSegment));
			if (_disposed) throw new ObjectDisposedException(nameof(BuddyAllocator<T>));

			ulong freeBlockIndexIndex = (ulong)(pSegment - _pData) / MinBlockLength;

			ref BlockHeader header = ref _pIndex[freeBlockIndexIndex];

			header = header with { Occupied = false };

			Allocations--;
			BlocksUsed -= BitOperations.RoundUpToPowerOf2(header.BlockLength);

#if TRACE_OUTPUT
			Console.WriteLine($"Returning block of length {header.BlockLength} at index {freeBlockIndexIndex}.");
#endif

			void Combine(ulong blockIndexIndex, int lengthLog)
			{
				ref BlockHeader header = ref _pIndex[blockIndexIndex];

				var buddyBlockIndexIndex = blockIndexIndex ^ (1ul << lengthLog);

				if (buddyBlockIndexIndex >= _indexLength)
				{
					// No buddy; at the end of the buffer.
					var nextFree = _freeBlockIndexesStart[lengthLog] == blockIndexIndex ? ulong.MaxValue : _freeBlockIndexesStart[lengthLog];
					header = header with { Occupied = false, BlockLengthLog = lengthLog, NextFree = nextFree, PreviousFree = ulong.MaxValue };
					_freeBlockIndexesStart[lengthLog] = blockIndexIndex;

					if (nextFree != ulong.MaxValue)
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
					var nextFree = _freeBlockIndexesStart[lengthLog] == blockIndexIndex ? ulong.MaxValue : _freeBlockIndexesStart[lengthLog];
					header = header with { Occupied = false, BlockLengthLog = lengthLog, NextFree = nextFree, PreviousFree = ulong.MaxValue };
					_freeBlockIndexesStart[lengthLog] = blockIndexIndex;

					if (nextFree != ulong.MaxValue)
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
			if (header.NextFree != ulong.MaxValue)
			{
				ref BlockHeader nextHeader = ref _pIndex[header.NextFree];

				nextHeader = nextHeader with { PreviousFree = header.PreviousFree };

				Debug.Assert(nextHeader.Occupied == false);

				if (header.PreviousFree != ulong.MaxValue)
				{
					ref BlockHeader prevHeader = ref _pIndex[header.PreviousFree];

					prevHeader = prevHeader with { NextFree = header.NextFree };
				}
				else
				{
					_freeBlockIndexesStart[header.BlockLengthLog] = header.NextFree;
				}
			}
			else if (header.PreviousFree != ulong.MaxValue)
			{
				ref BlockHeader prevHeader = ref _pIndex[header.PreviousFree];

				prevHeader = prevHeader with { NextFree = ulong.MaxValue };
			}
			else
			{
				_freeBlockIndexesStart[header.BlockLengthLog] = ulong.MaxValue;
			}
		}

		public void PrintBlocks()
		{
			for(ulong i=0; i<_indexLength; i++)
			{
				Console.Write(_pIndex[i].Occupied ? 'X' : '_');
			}

			Console.WriteLine();
		}




		public static ulong GetLengthRequiredToPreventDefragmentation(ulong maxLength, ulong maxBlockSize)
		{
			// Defoe, Delvin C., "Storage Coalescing" Report Number: WUCSE-2003-69 (2003). All Computer Science and Engineering Research.
			// https://openscholarship.wustl.edu/cse_research/1115 
			// M(log n + 2)/2
			return (maxLength * ((ulong)Math.Log(maxBlockSize) + 2)) >> 1;
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
					NativeMemory.Free(_pData);
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
