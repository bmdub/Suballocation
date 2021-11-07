
#define TRACE_OUTPUT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


// Sequential fit
// Stack + reverse (for double stack).  Can remove from end or clear.
// stack for single size?
// Ring buffer
// .net GC
// reversible sequential fit / below

// update window(s).  need segment graph to merge? best to merge on demand. just return list.


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
    public unsafe sealed class LocalityAllocator<T> : ISuballocator<T>, IDisposable where T : unmanaged
    {
        public readonly long BlockLength;
        private readonly T* _pElems;
        private readonly MemoryHandle _memoryHandle;
        private readonly bool _privatelyOwned;
        private readonly long _blockCount;
        private readonly Dictionary<long, long> _occupiedIndexes = new();
        private readonly Stack<IndexEntry> _prevIndexes = new();
        private readonly Stack<IndexEntry> _nextIndexes = new();
        private long _balance;
        private long _head;
        private bool _disposed;

        public LocalityAllocator(long length, long blockLength, bool zeroed = false)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
            if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length of <= 0.");
            if (blockLength > length) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length that's larger than {nameof(length)}.");

            LengthTotal = length;
            BlockLength = blockLength;
            _blockCount = length / blockLength;

            if (zeroed)
            {
                _pElems = (T*)NativeMemory.AllocZeroed((nuint)length, (nuint)Unsafe.SizeOf<T>());
            }
            else
            {
                _pElems = (T*)NativeMemory.Alloc((nuint)length, (nuint)Unsafe.SizeOf<T>());
            }

            _privatelyOwned = true;

            _nextIndexes.Push(new IndexEntry() { Offset = 0, Length = _blockCount * blockLength });
            _balance = _nextIndexes.Peek().Length;
        }

        public LocalityAllocator(T* pData, long length, long blockLength)
        {
            if (pData == null) throw new ArgumentNullException(nameof(pData));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot allocate a backing buffer of size <= 0.");
            if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length of <= 0.");
            if (blockLength > length) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length that's larger than {nameof(length)}.");

            LengthTotal = length;
            BlockLength = blockLength;
            _blockCount = length / blockLength;
            _pElems = pData;

            _nextIndexes.Push(new IndexEntry() { Offset = 0, Length = _blockCount * blockLength });
            _balance = _nextIndexes.Peek().Length;
        }

        public LocalityAllocator(Memory<T> data, long blockLength)
        {
            if (blockLength <= 0) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length of <= 0.");
            if (blockLength > data.Length) throw new ArgumentOutOfRangeException(nameof(blockLength), $"Cannot use a block length that's larger than {nameof(data.Length)}.");

            LengthTotal = data.Length;
            BlockLength = blockLength;
            _blockCount = LengthTotal / blockLength;
            _memoryHandle = data.Pin();
            _pElems = (T*)_memoryHandle.Pointer;

            _nextIndexes.Push(new IndexEntry() { Offset = 0, Length = _blockCount * blockLength });
            _balance = _nextIndexes.Peek().Length;
        }

        public long SizeUsed => LengthUsed * (long)Unsafe.SizeOf<T>();

        public long SizeTotal => LengthTotal * (long)Unsafe.SizeOf<T>();

        public long LengthUsed { get; private set; }

        public long LengthTotal { get; init; }

        public long Allocations { get; private set; }

        public UnmanagedMemorySegmentResource<T> RentResource(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new UnmanagedMemorySegmentResource<T>(this, _pElems + rawSegment.Offset, rawSegment.Length);
        }

        public void ReturnResource(UnmanagedMemorySegmentResource<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        public UnmanagedMemorySegment<T> Rent(long length = 1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), $"Cannot rent a segment of size 0.");

            var rawSegment = Alloc(length);

            return new UnmanagedMemorySegment<T>(_pElems + rawSegment.Offset, rawSegment.Length);
        }

        public void Return(UnmanagedMemorySegment<T> segment)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalityAllocator<T>));

            Free(segment.PElems - _pElems, segment.Length);
        }

        private unsafe (long Offset, long Length) Alloc(long length)
        {
#if TRACE_OUTPUT
            Console.WriteLine($"Rent length desired: {length:N0}");
#endif

            long blockLength = (length / BlockLength) * BlockLength;
            if (blockLength < length)
            {
                blockLength += BlockLength;
            }

            while (_prevIndexes.TryPeek(out var previousEntryForMove) && previousEntryForMove.Offset >= _head)
            {
                _nextIndexes.Push(_prevIndexes.Pop());

                if (_occupiedIndexes.ContainsKey(previousEntryForMove.Offset) == false)
                    _balance += (long)previousEntryForMove.Length << 1;
            }

            while (_nextIndexes.TryPeek(out var nextEntryForMove) && nextEntryForMove.Offset < _head)
            {
                _prevIndexes.Push(_nextIndexes.Pop());

                if (_occupiedIndexes.ContainsKey(nextEntryForMove.Offset) == false)
                    _balance -= (long)nextEntryForMove.Length << 1;
            }

            for (; ; )
            {
                if (_prevIndexes.TryPop(out var previousEntry1) == false)
                {
                    break;
                }

                if (_prevIndexes.TryPeek(out var previousEntry2) == true
                    && _occupiedIndexes.ContainsKey(previousEntry1.Offset) == false
                    && _occupiedIndexes.ContainsKey(previousEntry2.Offset) == false
                    && previousEntry2.Offset + previousEntry2.Length == previousEntry1.Offset)
                {
                    _prevIndexes.Pop();
                    _prevIndexes.Push(new IndexEntry() { Offset = previousEntry2.Offset, Length = previousEntry2.Length + previousEntry1.Length });
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
                    && _occupiedIndexes.ContainsKey(nextEntry1.Offset) == false
                    && _occupiedIndexes.ContainsKey(nextEntry2.Offset) == false
                    && nextEntry1.Offset + nextEntry1.Length == nextEntry2.Offset)
                {
                    _nextIndexes.Pop();
                    _nextIndexes.Push(new IndexEntry() { Offset = nextEntry1.Offset, Length = nextEntry1.Length + nextEntry2.Length });
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
                    if (nextEntry.Length >= length && _occupiedIndexes.ContainsKey(nextEntry.Offset) == false)
                    {
                        found = true;
                        emptyEntry = nextEntry;
                        _balance -= (long)nextEntry.Length;
                        break;
                    }
                    else
                    {
                        _prevIndexes.Push(nextEntry);

                        if (_occupiedIndexes.ContainsKey(nextEntry.Offset) == false)
                            _balance -= (long)nextEntry.Length << 1;
                    }
                }
            }

            void GetPrev()
            {
                while (_prevIndexes.TryPop(out var prevEntry) == true)
                {
                    if (prevEntry.Length >= length && _occupiedIndexes.ContainsKey(prevEntry.Offset) == false)
                    {
                        found = true;
                        emptyEntry = prevEntry;
                        _balance += (long)prevEntry.Length;
                        break;
                    }
                    else
                    {
                        _nextIndexes.Push(prevEntry);

                        if (_occupiedIndexes.ContainsKey(prevEntry.Offset) == false)
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
            long offset = emptyEntry.Offset;
            if (emptyEntry.Length > blockLength)
            {
                if (emptyEntry.Offset < _head)
                {
                    // Swap places with the new entry, to be closer to the current index
                    _prevIndexes.Push(new IndexEntry() { Offset = emptyEntry.Offset, Length = emptyEntry.Length - blockLength });

                    _balance -= (long)_prevIndexes.Peek().Length;

                    offset = (emptyEntry.Offset + emptyEntry.Length) - blockLength;
                }
                else
                {
                    _nextIndexes.Push(new IndexEntry() { Offset = emptyEntry.Offset + blockLength, Length = emptyEntry.Length - blockLength });

                    _balance += (long)_nextIndexes.Peek().Length;
                }

#if TRACE_OUTPUT
                Console.WriteLine($"Splitting out block of length {emptyEntry.Length - blockLength}.");
#endif
            }

            if (emptyEntry.Offset < _head)
            {
                _prevIndexes.Push(new IndexEntry() { Offset = offset, Length = blockLength });
            }
            else
            {
                _nextIndexes.Push(new IndexEntry() { Offset = offset, Length = blockLength });
            }

            _occupiedIndexes.Add(offset, blockLength);

#if TRACE_OUTPUT
            Console.WriteLine($"Free block of count {blockLength} found at offset {offset}.");
#endif

            _head = offset + blockLength;

            Allocations++;
            LengthUsed += blockLength;

            return new (offset, blockLength);
        }

        private unsafe void Free(long offset, long length)
        {
            if (_occupiedIndexes.Remove(offset, out var foundLength) == false)
            {
                throw new ArgumentException($"No rented segment at offset {offset} found.");
            }

            if (length != foundLength)
            {
                throw new ArgumentException($"Rented segment returned does not have expected length ({foundLength:N0}).");
            }

            if (offset >= _head)
                _balance += foundLength;
            else
                _balance -= foundLength;

            Allocations--;
            LengthUsed -= length;

#if TRACE_OUTPUT
            Console.WriteLine($"Returning block of length {length} at offset {offset}.");
#endif
        }

#if TRACE_OUTPUT
        public void PrintBlocks()
        {
            var list = _prevIndexes
                .Concat(_nextIndexes)
                .OrderBy(entry => entry.Offset)
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                if (i < list.Count)
                {
                    char c = _occupiedIndexes.TryGetValue(list[i].Offset, out _) ? 'X' : '_';
                    for (long j = 0; j < list[i].Length / BlockLength; j++)
                        Console.Write(c);
                }
            }

            Console.WriteLine();
        }
#endif

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

                _memoryHandle.Dispose(); //todo: if default, what will happen?

                if (_privatelyOwned)
                {
                    NativeMemory.Free(_pElems);
                }

                _disposed = true;
            }
        }

        ~LocalityAllocator()
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

            public long Offset { get => _offset; init => _offset = value; }
            public long Length { get => _length; init => _length = value; }
        }
    }
}
