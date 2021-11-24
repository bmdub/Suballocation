
namespace Suballocation.Collections;

/// <summary>
/// An unmanaged memory-backed stack that can store more than 2^31 elements.
/// </summary>
public unsafe class NativeStack<T> : IDisposable where T : unmanaged
{
    private T* _pElems;
    private long _bufferLength;
    private long _head;
    private bool _disposed;

    /// <summary></summary>
    /// <param name="initialCapacity"></param>
    public NativeStack(long initialCapacity = 4)
    {
        _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
        _bufferLength = initialCapacity;
    }

    /// <summary>The number of elements in the queue.</summary>
    public long Count => _head;

    /// <summary>Adds an element to the top of the stack.</summary>
    /// <param name="elem"></param>
    public void Push(T elem)
    {
        if (_head == _bufferLength)
        {
            // Double the size of the backing buffer, and copy over existing elements.
            var pElemsNew = (T*)NativeMemory.Alloc((nuint)_bufferLength << 1, (nuint)Unsafe.SizeOf<T>());
            Buffer.MemoryCopy(_pElems, pElemsNew, _bufferLength * Unsafe.SizeOf<T>(), _bufferLength * Unsafe.SizeOf<T>());
            NativeMemory.Free(_pElems);
            _pElems = pElemsNew;

            _bufferLength <<= 1;
        }

        _pElems[_head] = elem;
        _head++;
    }

    /// <summary>Removes and returns the element at the top of the stack.</summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public T Pop()
    {
        if (TryPop(out var value) == false)
        {
            throw new InvalidOperationException("Stack has 0 elements.");
        }

        return value;
    }

    /// <summary>Removes and returns the element at the top of the stack.</summary>
    /// <param name="value">The value, if any.</param>
    /// <returns>True if successful.</returns>
    public bool TryPop(out T value)
    {
        if (_head == 0)
        {
            value = default;
            return false;
        }

        _head--;
        value = _pElems[_head];

        return true;
    }

    /// <summary>Returns the element at the top of the stack, without removal.</summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public T Peek()
    {
        if (TryPeek(out var value) == false)
        {
            throw new InvalidOperationException("Stack has 0 elements.");
        }

        return value;
    }

    /// <summary>Returns the element at the top of the stack, without removal.</summary>
    /// <param name="value">The value, if any.</param>
    /// <returns>True if successful.</returns>
    public bool TryPeek(out T value)
    {
        if (_head == 0)
        {
            value = default;
            return false;
        }

        value = _pElems[_head - 1];

        return true;
    }

    /// <summary>Removes all elements of the stack, without resizing the backing buffer.</summary>
    public void Clear()
    {
        _head = 0;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            NativeMemory.Free(_pElems);
        }

        _disposed = true;
    }

    ~NativeStack()
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
