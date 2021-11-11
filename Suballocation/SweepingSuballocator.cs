
//#define TRACE_OUTPUT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

//https://docs.microsoft.com/en-us/archive/msdn-magazine/2000/december/garbage-collection-part-2-automatic-memory-management-in-the-microsoft-net-framework 
// The newer an object is, the shorter its lifetime will be.
// The older an object is, the longer its lifetime will be.
// Newer objects tend to have strong relationships to each other and are frequently accessed around the same time.
// Compacting a portion of the heap is faster than compacting the whole heap.

//me
// The larger an object is, the longer its lifetime will be.
// The larger an object is, the larger the acceptable update window / placement distance.

//todo: update window(s)
// compaction
// also option for full compaction?
// consider segment updates, which will affect the update window. combine with compaction?
// will starting capacities make better efficiency?

// Generational
// ..... 888 55 3 2 1 0 1 2 3 55 888 .....
// ........ 555 22 0 1 33 88888

// buddy system
// use heap for free list, 1 for each size and side.  pop and push as needed to get nearest for given size.
// still could be quite far away... maybe do that operation for all sizes higher that are not empty, then decide.
// need custom heap

// update window(s).  need segment graph to merge? best to merge on demand. just return list.


// tests
// varying buffer sizes
// varying allocation sizes
// different fixed levels of allocation sizes
// random free/rent for fixed level allocations
// random free/rent for varying alloc sizes
// varying block sizes (for varying alloc tests)
// compaction vs without
// overprovisioning needed

// stats
// failed with OOO count
// compaction rate
// update windows / locality of rentals
// free space when OOO
// amt data compacted
// other mem usage

// movement strategy objects
// given stats on: side with largest free segment, side with most free sum, head dist from center, 

// fragmentation strategy objects
// pushing away from center

// single chain of used/free segments infos.
// both are combined when in runs.
// ptr at each latest alloc point.  on move, record nearest free segment on each side for each size (bucketed) (llists? or heaps?).
// also store compaction potential to each segment? compaction amount / data moved
// choose bucket: weight by is side with most free space left, and distance from ptr as a function of data written for this range, and is non-ideal bucket, compaction potential (and range), 
//   For every n growth from the range start to one side, can alloc n/x of the other side. go from out to in? fib or 2^n?
// when to compact?



namespace Suballocation
{
    // reversible sequential fit?
    public unsafe sealed class SweepingSuballocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        public readonly long BlockLength;
        private readonly T* _pElems;
        private readonly MemoryHandle _memoryHandle;
        private readonly bool _privatelyOwned;
        private readonly long _blockCount;
        private readonly NativeBitArray _allocatedIndexes;
        private readonly NativeStack<IndexEntry> _prevIndexes = new();
        private readonly NativeStack<IndexEntry> _nextIndexes = new();
        private long _balance;
        private long _head;
        private bool _disposed;

        public SweepingSuballocator(long length, long blockLength)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
            if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length of <= 0.");
            if (blockLength > length) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length that's larger than {nameof(length)}.");
            
            LengthTotal = length;
            BlockLength = blockLength;
            _blockCount = length / blockLength;
            _allocatedIndexes = new NativeBitArray(length);

            _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
            _privatelyOwned = true;

            _nextIndexes.Push(new IndexEntry() { Index = 0, Length = _blockCount * blockLength });
            _balance = _nextIndexes.Peek().Length;
        }

        public SweepingSuballocator(T* pData, long length, long blockLength)
        {
            if (pData == null) throw new ArgumentNullException(nameof(pData));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
            if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length of <= 0.");
            if (blockLength > length) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length that's larger than {nameof(length)}.");

            LengthTotal = length;
            BlockLength = blockLength;
            _blockCount = length / blockLength;
            _allocatedIndexes = new NativeBitArray(length);

            _pElems = pData;

            _nextIndexes.Push(new IndexEntry() { Index = 0, Length = _blockCount * blockLength });
            _balance = _nextIndexes.Peek().Length;
        }

        public SweepingSuballocator(Memory<T> data, long blockLength)
        {
            if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length of <= 0.");
            if (blockLength > data.Length) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length that's larger than {nameof(data.Length)}.");

            LengthTotal = data.Length;
            BlockLength = blockLength;
            _blockCount = LengthTotal / blockLength;
            _memoryHandle = data.Pin();
            _allocatedIndexes = new NativeBitArray(data.Length);

            _pElems = (T*)_memoryHandle.Pointer;

            _nextIndexes.Push(new IndexEntry() { Index = 0, Length = _blockCount * blockLength });
            _balance = _nextIndexes.Peek().Length;
        }

        public long SizeUsed => LengthUsed * Unsafe.SizeOf<T>();

        public long SizeTotal => LengthTotal * Unsafe.SizeOf<T>();

        public long Allocations { get; private set; }

        public long LengthUsed { get; private set; }

        public long LengthTotal { get; init; }

        public T* PElems => _pElems;

        public NativeMemorySegmentResource<T> RentResource(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new NativeMemorySegmentResource<T>(this, _pElems + rawSegment.Index, rawSegment.Length);
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

            return new NativeMemorySegment<T>(_pElems + rawSegment.Index, rawSegment.Length);
        }

        public void Return(NativeMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SweepingSuballocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        private unsafe (long Index, long Length) Alloc(long length)
        {
#if TRACE_OUTPUT
            Console.WriteLine($"Rent length desired: {length:N0}");
#endif

            long blockLength = (length / BlockLength) * BlockLength;
            if (blockLength < length)
            {
                blockLength += BlockLength;
            }

            while (_prevIndexes.TryPeek(out var previousEntryForMove) && previousEntryForMove.Index >= _head)
            {
                _nextIndexes.Push(_prevIndexes.Pop());

                if (_allocatedIndexes[previousEntryForMove.Index] == false)
                    _balance += (long)previousEntryForMove.Length << 1;
            }

            while (_nextIndexes.TryPeek(out var nextEntryForMove) && nextEntryForMove.Index < _head)
            {
                _prevIndexes.Push(_nextIndexes.Pop());

                if (_allocatedIndexes[nextEntryForMove.Index] == false)
                    _balance -= (long)nextEntryForMove.Length << 1;
            }

            for (; ; )
            {
                if (_prevIndexes.TryPop(out var previousEntry1) == false)
                {
                    break;
                }

                if (_prevIndexes.TryPeek(out var previousEntry2) == true
                    && _allocatedIndexes[previousEntry1.Index] == false
                    && _allocatedIndexes[previousEntry2.Index] == false
                    && previousEntry2.Index + previousEntry2.Length == previousEntry1.Index)
                {
                    _prevIndexes.Pop();
                    _prevIndexes.Push(new IndexEntry() { Index = previousEntry2.Index, Length = previousEntry2.Length + previousEntry1.Length });
                }
                else
                {
                    _prevIndexes.Push(previousEntry1);
                    break;
                }
            }

            for (; ; )
            {
                if (_nextIndexes.TryPop(out var nextEntry1) == false)
                {
                    break;
                }

                if (_nextIndexes.TryPeek(out var nextEntry2) == true
                    && _allocatedIndexes[nextEntry1.Index] == false
                    && _allocatedIndexes[nextEntry2.Index] == false
                    && nextEntry1.Index + nextEntry1.Length == nextEntry2.Index)
                {
                    _nextIndexes.Pop();
                    _nextIndexes.Push(new IndexEntry() { Index = nextEntry1.Index, Length = nextEntry1.Length + nextEntry2.Length });
                }
                else
                {
                    _nextIndexes.Push(nextEntry1);
                    break;
                }
            }

            IndexEntry emptyEntry = default;
            bool found = false;

            void GetNext()
            {
                while (_nextIndexes.TryPop(out var nextEntry) == true)
                {
                    if (nextEntry.Length >= length && _allocatedIndexes[nextEntry.Index] == false)
                    {
                        found = true;
                        emptyEntry = nextEntry;
                        _balance -= (long)nextEntry.Length;
                        break;
                    }
                    else
                    {
                        _prevIndexes.Push(nextEntry);

                        if (_allocatedIndexes[nextEntry.Index] == false)
                            _balance -= (long)nextEntry.Length << 1;
                    }
                }
            }

            void GetPrev()
            {
                while (_prevIndexes.TryPop(out var prevEntry) == true)
                {
                    if (prevEntry.Length >= length && _allocatedIndexes[prevEntry.Index] == false)
                    {
                        found = true;
                        emptyEntry = prevEntry;
                        _balance += (long)prevEntry.Length;
                        break;
                    }
                    else
                    {
                        _nextIndexes.Push(prevEntry);

                        if (_allocatedIndexes[prevEntry.Index] == false)
                            _balance += (long)prevEntry.Length << 1;
                    }
                }
            }

            if (_balance >= 0)
            {
                GetNext();
                if (found == false) GetPrev();
            }
            else
            {
                GetPrev();
                if (found == false) GetNext();
            }

            if (found == false)
            {
                throw new OutOfMemoryException();
            }

            // Split out an empty entry if required size is less than the block given
            long offset = emptyEntry.Index;
            if (emptyEntry.Length > blockLength)
            {
                if (emptyEntry.Index < _head)
                {
                    // Swap places with the new entry, to be closer to the current index
                    _prevIndexes.Push(new IndexEntry() { Index = emptyEntry.Index, Length = emptyEntry.Length - blockLength });

                    _balance -= (long)_prevIndexes.Peek().Length;

                    offset = (emptyEntry.Index + emptyEntry.Length) - blockLength;
                }
                else
                {
                    _nextIndexes.Push(new IndexEntry() { Index = emptyEntry.Index + blockLength, Length = emptyEntry.Length - blockLength });

                    _balance += (long)_nextIndexes.Peek().Length;
                }

#if TRACE_OUTPUT
                Console.WriteLine($"Splitting out block of length {emptyEntry.Length - blockLength}.");
#endif
            }

            if (emptyEntry.Index < _head)
            {
                _prevIndexes.Push(new IndexEntry() { Index = offset, Length = blockLength });
            }
            else
            {
                _nextIndexes.Push(new IndexEntry() { Index = offset, Length = blockLength });
            }

            _allocatedIndexes[offset] = true;

#if TRACE_OUTPUT
            Console.WriteLine($"Free block of count {blockLength} found at offset {offset}.");
#endif

            _head = offset + blockLength;

            Allocations++;
            LengthUsed += blockLength;

            return new(offset, blockLength);
        }

        private unsafe void Free(long offset, long length)
        {
            if (_allocatedIndexes[offset] == false)
            {
                throw new ArgumentException($"No rented segment at offset {offset} found.");
            }

            if (offset >= _head)
                _balance += length;
            else
                _balance -= length;

            Allocations--;
            LengthUsed -= length;

#if TRACE_OUTPUT
            Console.WriteLine($"Returning block of length {length} at offset {offset}.");
#endif
        }

        public void Clear()
        {
            Allocations = 0;
            LengthUsed = 0;
            _nextIndexes.Clear();
            _prevIndexes.Clear();
            _allocatedIndexes.Clear();
            _nextIndexes.Push(new IndexEntry() { Index = 0, Length = LengthTotal });
            _balance = _nextIndexes.Peek().Length;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _nextIndexes.Dispose();
                    _prevIndexes.Dispose();
                    _allocatedIndexes.Dispose();
                }

                _memoryHandle.Dispose(); //todo: if default, what will happen?

                if (_privatelyOwned)
                {
                    NativeMemory.Free(_pElems);
                }

                _disposed = true;
            }
        }

        ~SweepingSuballocator()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        readonly struct IndexEntry
        {
            private readonly long _offset;
            private readonly long _length;

            public long Index { get => _offset; init => _offset = value; }
            public long Length { get => _length; init => _length = value; }
        }
    }
}
