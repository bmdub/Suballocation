using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation.Collections;

internal unsafe class NativeHeap<T> : IDisposable where T : unmanaged
{
    private T* _pElems;
    private long _bufferLength;
    private long _tail;
    private IComparer<T> _comparer;
    private bool _disposed;

    public NativeHeap(long initialCapacity = 4, IComparer<T>? comparer = default)
    {
        _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
        _bufferLength = initialCapacity;
        _comparer = comparer == default ? Comparer<T>.Default : comparer;
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
        long parentIndex = index - 1 >> 1;
        while (parentIndex >= 0 && _comparer.Compare(_pElems[parentIndex], _pElems[index]) > 0)
        {
            _pElems[index] = _pElems[parentIndex];
            _pElems[parentIndex] = elem;

            index = parentIndex;
            parentIndex = index - 1 >> 1;
        }

        _tail++;
    }

    public T Dequeue()
    {
        if (TryDequeue(out var value) == false)
        {
            throw new InvalidOperationException("Heap has 0 elements.");
        }

        return value;
    }

    public bool TryDequeue(out T value)
    {
        if (_tail == 0)
        {
            value = default;
            return false;
        }

        value = _pElems[0];

        _tail--;

        if (_tail > 0)
        {
            _pElems[0] = _pElems[_tail];

            long index = 0;
            for (; ; )
            {
                long childIndex1 = (index << 2) + 1;
                long childIndex2 = (index << 2) + 2;

                if (childIndex2 >= _tail)
                {
                    if (childIndex1 < _tail)
                    {
                        if (_comparer.Compare(_pElems[index], _pElems[childIndex1]) > 0)
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
                    long bestIndex = _comparer.Compare(_pElems[childIndex1], _pElems[childIndex2]) < 0 ? childIndex1 : childIndex2;
                    if (_comparer.Compare(_pElems[index], _pElems[bestIndex]) < 0)
                        break;
                    var temp = _pElems[index];
                    _pElems[index] = _pElems[bestIndex];
                    _pElems[bestIndex] = temp;
                    index = bestIndex;
                }
            }
        }

        return true;
    }

    public T Peek()
    {
        if (TryPeek(out var value) == false)
        {
            throw new InvalidOperationException("Heap has 0 elements.");
        }

        return value;
    }

    public bool TryPeek(out T item)
    {
        if (_tail == 0)
        {
            item = default;
            return false;
        }

        item = _pElems[0];

        return true;
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
