
namespace Suballocation.Collections;

/// <summary>
/// An unmanaged memory-backed min heap that can store more than 2^31 elements.
/// </summary>
public unsafe class NativeHeap<T> : IDisposable where T : unmanaged
{
    private readonly IComparer<T> _comparer;
    private T* _pElems;
    private long _bufferLength;
    private long _tail;
    private bool _disposed;

    /// <summary></summary>
    /// <param name="initialCapacity">Sets the size of the initial backing allocation.</param>
    /// <param name="comparer">A custom comparer to determine the "minimum". If not specified, the default Comparer<typeparamref name="T"/> will be used.</param>
    public NativeHeap(long initialCapacity = 4, IComparer<T>? comparer = default)
    {
        _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
        _bufferLength = initialCapacity;
        _comparer = comparer == default ? Comparer<T>.Default : comparer;
    }

    /// <summary>The number of elements in the heap.</summary>
    public long Count => _tail;

    /// <summary>Adds an element to the heap.</summary>
    /// <param name="elem"></param>
    public void Enqueue(T elem)
    {
        // If the heap is full, double the size of the backing buffer.
        if (_tail == _bufferLength - 1)
        {
            var pElemsNew = (T*)NativeMemory.Alloc((nuint)_bufferLength << 1, (nuint)Unsafe.SizeOf<T>());
            Buffer.MemoryCopy(_pElems, pElemsNew, _bufferLength * Unsafe.SizeOf<T>(), _bufferLength * Unsafe.SizeOf<T>());
            NativeMemory.Free(_pElems);
            _pElems = pElemsNew;

            _bufferLength <<= 1;
        }

        // Insert into the heap.
        _pElems[_tail] = elem;

        long index = _tail;
        long parentIndex = (index - 1) >> 1;
        while (parentIndex >= 0 && _comparer.Compare(_pElems[parentIndex], _pElems[index]) > 0)
        {
            _pElems[index] = _pElems[parentIndex];
            _pElems[parentIndex] = elem;

            index = parentIndex;
            parentIndex = (index - 1) >> 1;
        }

        _tail++;
    }

    /// <summary>Removes and returns the comparatively minimum value from the heap.</summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public T Dequeue()
    {
        if (TryDequeue(out var value) == false)
        {
            throw new InvalidOperationException("Heap has 0 elements.");
        }

        return value;
    }

    /// <summary>Removes and returns the comparatively minimum value from the heap.</summary>
    /// <param name="value">The value, if any.</param>
    /// <returns>True if successful.</returns>
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
                long childIndex1 = (index << 1) + 1;
                long childIndex2 = (index << 1) + 2;

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

    /// <summary>Returns the comparatively minimum value from the heap, without removal.</summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public T Peek()
    {
        if (TryPeek(out var value) == false)
        {
            throw new InvalidOperationException("Heap has 0 elements.");
        }

        return value;
    }

    /// <summary>Returns the comparatively minimum value from the heap, without removal.</summary>
    /// <param name="value">The value, if any.</param>
    /// <returns>True if successful.</returns>
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

    /// <summary>Removes all elements from the heap, without resizing.</summary>
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

    /// <summary>Disposes unmanaged resources used by this collection.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
