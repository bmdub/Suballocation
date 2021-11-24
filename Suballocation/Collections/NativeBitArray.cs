using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation.Collections;

public unsafe sealed class NativeBitArray : IDisposable
{
    public long Length { get; private set; }
    private const ulong BitOffsetMask = 64 - 1;
    private long _lengthLongs;
    private ulong* _pData;
    private bool _disposedValue;

    public NativeBitArray(long length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0.");

        Length = length;
        _lengthLongs = length / 64;
        if (_lengthLongs * 64 != length) _lengthLongs++;

        _pData = (ulong*)NativeMemory.AllocZeroed((nuint)_lengthLongs * 8);
    }

    public bool this[long index]
    {
        get
        {
            if (_disposedValue) throw new ObjectDisposedException(nameof(NativeBitArray));
            if (index < 0 || index >= Length) throw new ArgumentOutOfRangeException(nameof(index));

            long longIndex = index >> 6;
            var bitMask = 1ul << unchecked((int)((ulong)index & BitOffsetMask));

            return (_pData[longIndex] & bitMask) == bitMask;
        }
        set
        {
            if (_disposedValue) throw new ObjectDisposedException(nameof(NativeBitArray));
            if (index < 0 || index >= Length) throw new ArgumentOutOfRangeException(nameof(index));

            long byteIndex = index >> 6;
            var bitMask = 1ul << unchecked((int)((ulong)index & BitOffsetMask));

            if (value)
            {
                _pData[byteIndex] |= unchecked(bitMask);
            }
            else
            {
                _pData[byteIndex] &= unchecked(~bitMask);
            }
        }
    }

    public void Resize(long bitLength)
    {
        if (_disposedValue) throw new ObjectDisposedException(nameof(NativeBitArray));
        if (bitLength < 0) throw new ArgumentOutOfRangeException(nameof(bitLength), "Length must be >= 0.");

        var oldPData = _pData;

        var prevLengthBytes = _lengthLongs * 8;

        Length = bitLength;
        _lengthLongs = bitLength / 64;
        if (_lengthLongs * 64 != bitLength) _lengthLongs++;

        _pData = (ulong*)NativeMemory.AllocZeroed((nuint)_lengthLongs * 8);

        var lengthBytes = Math.Min(prevLengthBytes, _lengthLongs * 8);

        for (long i = 0; i < lengthBytes; i += uint.MaxValue)
        {
            uint lengthPart = (uint)Math.Min(uint.MaxValue, lengthBytes - i);

            Unsafe.CopyBlock(_pData + i, oldPData + i, lengthPart);
        }

        NativeMemory.Free(oldPData);
    }

    public void Clear()
    {
        if (_disposedValue) throw new ObjectDisposedException(nameof(NativeBitArray));

        var lengthBytes = _lengthLongs * 8;

        for (long i = 0; i < lengthBytes; i += uint.MaxValue)
        {
            uint length = (uint)Math.Min(uint.MaxValue, lengthBytes - i);

            Unsafe.InitBlock(_pData + i, 0, length);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
            }

            NativeMemory.Free(_pData);

            _disposedValue = true;
        }
    }

    ~NativeBitArray()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
