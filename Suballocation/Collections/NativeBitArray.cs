using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation.Collections;

//todo: use ulong instead of byte?

internal unsafe sealed class NativeBitArray : IDisposable
{
    public long Length;
    public long LengthBytes;
    private const long BitOffsetMask = 0x07;
    private byte* _pData;
    private bool _disposedValue;

    public NativeBitArray(long length)
    {
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be 0.");

        Length = length;
        LengthBytes = length / 8;
        if ((length & 255) != 0) LengthBytes++;

        _pData = (byte*)NativeMemory.AllocZeroed((nuint)LengthBytes);
    }

    public bool this[long index]
    {
        get
        {
            long byteIndex = index >> 3;
            var bitMask = 1u << unchecked((int)(index & BitOffsetMask));

            return (_pData[byteIndex] & bitMask) == bitMask;
        }
        set
        {
            long byteIndex = index >> 3;
            var bitMask = 1u << unchecked((int)(index & BitOffsetMask));

            if (value)
            {
                _pData[byteIndex] |= unchecked((byte)bitMask);
            }
            else
            {
                _pData[byteIndex] &= unchecked((byte)~bitMask);
            }
        }
    }

    public void Resize(long length)
    {
        var oldPData = _pData;

        Length = length;
        LengthBytes = length / 8;
        if ((length & 255) != 0) LengthBytes++;

        _pData = (byte*)NativeMemory.AllocZeroed((nuint)LengthBytes);

        for (long i = 0; i < LengthBytes; i += uint.MaxValue)
        {
            uint lengthPart = (uint)Math.Min(uint.MaxValue, LengthBytes - i);

            Unsafe.CopyBlock(_pData + i, oldPData + i, lengthPart);
        }

        NativeMemory.Free(_pData);
    }

    public void Clear()
    {
        for (long i = 0; i < LengthBytes; i += uint.MaxValue)
        {
            uint length = (uint)Math.Min(uint.MaxValue, LengthBytes - i);

            Unsafe.InitBlock(_pData + i, 0, length);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
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
