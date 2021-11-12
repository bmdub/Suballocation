using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation.Collections;

internal unsafe class NativeStack<T> : IDisposable where T : unmanaged
{
    private T* _pElems;
    private long _bufferLength;
    private long _head;
    private bool _disposed;

    public NativeStack(long initialCapacity = 4)
    {
        _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
        _bufferLength = initialCapacity;
    }

    public long Length => _head;

    public void Push(T elem)
    {
        if (_head == _bufferLength)
        {
            var pElemsNew = (T*)NativeMemory.Alloc((nuint)_bufferLength << 1, (nuint)Unsafe.SizeOf<T>());
            Buffer.MemoryCopy(_pElems, pElemsNew, _bufferLength * Unsafe.SizeOf<T>(), _bufferLength * Unsafe.SizeOf<T>());
            NativeMemory.Free(_pElems);
            _pElems = pElemsNew;

            _bufferLength <<= 1;
        }

        _pElems[_head] = elem;
        _head++;
    }

    public T Pop()
    {
        if (TryPop(out var value) == false)
        {
            throw new InvalidOperationException("Stack has 0 elements.");
        }

        return value;
    }

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

    public T Peek()
    {
        if (TryPeek(out var value) == false)
        {
            throw new InvalidOperationException("Stack has 0 elements.");
        }

        return value;
    }

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

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
