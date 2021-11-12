using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation
{
    internal unsafe class NativeHeap<T> : IDisposable where T : unmanaged, IComparable<T>
    {
        private T* _pElems;
        private long _bufferLength;
        private long _tail;
        private bool _disposed;

        public NativeHeap(long initialCapacity = 4)
        {
            _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
            _bufferLength = initialCapacity;
        }

        public long Length => _tail;

        public void Enqueue(T elem)
        {
            if (_tail == _bufferLength - 1)
            {
                var pElemsNew = (T*)NativeMemory.Alloc((nuint)_bufferLength << 1, (nuint)Unsafe.SizeOf<T>());
                Buffer.MemoryCopy(_pElems, pElemsNew, _bufferLength * Unsafe.SizeOf<T>(), _bufferLength * Unsafe.SizeOf<T>());
                NativeMemory.Free(_pElems);
                _pElems = pElemsNew;

                _bufferLength <<= 1;
            }

            _pElems[_tail] = elem;

            long index = _tail;
            long parentIndex = (index - 1) >> 1;
            while (parentIndex >= 0 && _pElems[parentIndex].CompareTo(_pElems[index]) > 0)
            {
                _pElems[index] = _pElems[parentIndex];
                _pElems[parentIndex] = elem;

                index = parentIndex;
                parentIndex = (index - 1) >> 1;
            }

            _tail++;
        }

        public T Extract()
        {
            if (_tail == 0) throw new InvalidOperationException("Heap has 0 elements.");

            var value = _pElems[0];

            _tail--;

            if (_tail > 0)
            {
                _pElems[0] = _pElems[_tail];

                long index = 0;
                for (; ; )
                {
                    long childIndex1 = index * 2 + 1;
                    long childIndex2 = index * 2 + 2;

                    if (childIndex2 >= _tail)
                    {
                        if (childIndex1 < _tail)
                        {
                            if (_pElems[index].CompareTo(_pElems[childIndex1]) > 0)
                            {
                                var temp = _pElems[index];
                                _pElems[index] = _pElems[childIndex1];
                                _pElems[childIndex1] = temp;
                            }
                        }

                        break;
                    }
                    else
                    {
                        long bestIndex = _pElems[childIndex1].CompareTo(_pElems[childIndex2]) < 0 ? childIndex1 : childIndex2;
                        if (_pElems[index].CompareTo(_pElems[bestIndex]) < 0)
                            break;
                        var temp = _pElems[index];
                        _pElems[index] = _pElems[bestIndex];
                        _pElems[bestIndex] = temp;
                        index = bestIndex;
                    }
                }
            }

            /*int target = 1;
			for (int i = 0; i < _tail; i++)
			{
				Console.Write(_pElems[i] + " ");
				if (i + 1 == target)
				{
					Console.WriteLine();
					target = target * 2 + 1;
				}
			}*/

            return value;
        }

        public void Clear()
        {
            _tail = 0;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                NativeMemory.Free(_pElems);
            }

            _disposed = true;
        }

        ~NativeHeap()
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
