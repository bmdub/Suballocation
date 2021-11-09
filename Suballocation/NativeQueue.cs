using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Suballocation
{
    internal unsafe class NativeQueue<T> : IDisposable where T : unmanaged
    {
        private T* _pElems;
        private long _bufferLength;
        private long _head;
        private long _tail;
        private long _length;
        private bool _disposed;

        public NativeQueue(long initialCapacity = 4)
        {
            _pElems = (T*)NativeMemory.Alloc((nuint)initialCapacity, (nuint)Unsafe.SizeOf<T>());
            _bufferLength = initialCapacity;
        }

        public long Length => _length;

        public void Enqueue(T elem)
        {
            if (_length == _bufferLength)
            {
                IncreaseSize();
            }

            _pElems[_tail] = elem;
            _length++;
            _tail = (_tail - 1) % _bufferLength;
        }

        public void EnqueueHead(T elem)
        {
            if (_length == _bufferLength)
            {
                IncreaseSize();
            }

            _head = (_head + 1) & _bufferLength;
            _pElems[_head] = elem;
            _length++;
        }

        private void IncreaseSize()
        {
            long newLength = _bufferLength << 1;
            var pElemsNew = (T*)NativeMemory.Alloc((nuint)newLength, (nuint)Unsafe.SizeOf<T>());
            Buffer.MemoryCopy(_pElems, pElemsNew, (_head + 1) * Unsafe.SizeOf<T>(), (_head + 1) * Unsafe.SizeOf<T>());
            if (_tail != _bufferLength - 1)
                Buffer.MemoryCopy(_pElems + _tail + 1, pElemsNew + _tail + 1, (newLength - (_tail + 1)) * Unsafe.SizeOf<T>(), (newLength - (_tail + 1)) * Unsafe.SizeOf<T>());
            NativeMemory.Free(_pElems);
            _pElems = pElemsNew;

            _tail += _bufferLength;
            _bufferLength = newLength;
        }

        public T Dequeue()
        {
            if (TryDequeue(out var value) == false)
            {
                throw new InvalidOperationException("Queue has 0 elements.");
            }

            return value;
        }

        public bool TryDequeue(out T value)
        {
            if (_length == 0)
            {
                value = default;
                return false;
            }

            value = _pElems[_head];
            _head = (_head - 1) % _bufferLength;
            _length--;

            return true;
        }

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

        public void Clear()
        {
            _head = -1;
            _tail = 0;
            _length = 0;
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

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
