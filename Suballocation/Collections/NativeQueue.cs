
namespace Suballocation.Collections;

/// <summary>
/// An unmanaged memory-backed queue that can store more than 2^31 elements.
/// </summary>
public unsafe class NativeQueue<T> : IDisposable where T : unmanaged
{
    private T* _pElems;
    private long _bufferLength;
    private long _head;
    private long _tail;
    private long _length;
    private bool _disposed;

    /// <summary></summary>
    /// <param name="initialCapacity"></param>
    public NativeQueue(long initialCapacity = 4)
    {
        _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
        _bufferLength = initialCapacity;

        _head = _bufferLength - 1;
        _tail = _bufferLength - 1;
    }

    /// <summary>The number of elements in the queue.</summary>
    public long Count => _length;

    /// <summary>Adds an element to the tail of the queue.</summary>
    /// <param name="elem"></param>
    public void Enqueue(T elem)
    {
        if (_length == _bufferLength)
        {
            IncreaseSize();
        }

        _pElems[_tail] = elem;
        _length++;
        _tail = ModuloPositive(_tail - 1, _bufferLength);
    }

    /// <summary>Adds an element to the head of the "queue".</summary>
    /// <param name="elem"></param>
    public void EnqueueHead(T elem)
    {
        if (_length == _bufferLength)
        {
            IncreaseSize();
        }

        _head = (_head + 1) % _bufferLength;
        _pElems[_head] = elem;
        _length++;
    }

    /// <summary>Removes and returns the element at the head of the queue.</summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public T Dequeue()
    {
        if (TryDequeue(out var value) == false)
        {
            throw new InvalidOperationException("Queue has 0 elements.");
        }

        return value;
    }

    /// <summary>Removes and returns the element at the head of the queue.</summary>
    /// <param name="value">The value, if any.</param>
    /// <returns>True if successful.</returns>
    public bool TryDequeue(out T value)
    {
        if (_length == 0)
        {
            value = default;
            return false;
        }

        value = _pElems[_head];
        _head = ModuloPositive((_head - 1), _bufferLength);
        _length--;

        return true;
    }


    /// <summary>Returns the element at the head of the queue, without removal.</summary>
    /// <param name="value">The value, if any.</param>
    /// <returns>True if successful.</returns>
    public bool TryPeek(out T value)
    {
        if (_length == 0)
        {
            value = default;
            return false;
        }

        value = _pElems[_head];

        return true;
    }

    /// <summary>Removes all elements of the queue, without resizing the backing buffer.</summary>
    public void Clear()
    {
        _head = _bufferLength - 1;
        _tail = _bufferLength - 1;
        _length = 0;
    }

    private long ModuloPositive(long x, long m)
    {
        // Systems or languages may define modulo differently; we want negative x's to wrap around to positive.
        long r = x % m;
        return r < 0 ? r + m : r;
    }

    private void IncreaseSize()
    {
        // Double the size of the backing buffer, and copy over existing elements, adjusting pointers as needed.
        long newLength = _bufferLength << 1;
        var pElemsNew = (T*)NativeMemory.Alloc((nuint)newLength, (nuint)Unsafe.SizeOf<T>());

        if (_tail >= _head)
        {
            Buffer.MemoryCopy(_pElems, pElemsNew, (_head + 1) * Unsafe.SizeOf<T>(), (_head + 1) * Unsafe.SizeOf<T>());

            if (_tail < _bufferLength - 1)
            {
                Buffer.MemoryCopy(_pElems + _tail + 1, pElemsNew + _tail + 1 + _bufferLength, (newLength - (_tail + 1 + _bufferLength)) * Unsafe.SizeOf<T>(), (_bufferLength - (_tail + 1)) * Unsafe.SizeOf<T>());
            }

            _tail += _bufferLength;
        }
        else
        {
            Buffer.MemoryCopy(_pElems + _tail + 1, pElemsNew + _tail + 1, (newLength - (_tail + 1)) * Unsafe.SizeOf<T>(), (_head + 1 - (_tail + 1)) * Unsafe.SizeOf<T>());
        }

        NativeMemory.Free(_pElems);
        _pElems = pElemsNew;
        _bufferLength = newLength;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            NativeMemory.Free(_pElems);
        }

        _disposed = true;
    }

    ~NativeQueue()
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
