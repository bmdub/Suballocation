
namespace Suballocation.Collections;

/// <summary>
/// An unmanaged memory-backed bit array that can store more than 2^31 elements.
/// </summary>
public unsafe sealed class NativeBitArray : IDisposable
{
    public long Length { get; private set; }
    private const ulong BitOffsetMask = 64 - 1;
    private long _lengthLongs;
    private ulong* _pData;
    private bool _disposedValue;

    /// <summary></summary>
    /// <param name="length">The length, in bits, of the array.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public NativeBitArray(long length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0.");

        Length = length;
        _lengthLongs = length / 64;
        if (_lengthLongs * 64 != length)
        {
            _lengthLongs++;
        }

        _pData = (ulong*)NativeMemory.AllocZeroed((nuint)_lengthLongs * 8);
    }

    /// <summary></summary>
    /// <param name="index">The index of the ith bit.</param>
    /// <returns></returns>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
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

    /// <summary>Resize the array to a larger or smaller size, copying the existing elements.</summary>
    /// <param name="length">The length, in bits, of the array.</param>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void Resize(long length)
    {
        if (_disposedValue) throw new ObjectDisposedException(nameof(NativeBitArray));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0.");

        // Create the new array.
        var pOldData = _pData;

        var prevLengthBytes = _lengthLongs * 8;

        Length = length;
        _lengthLongs = length / 64;
        if (_lengthLongs * 64 != length) _lengthLongs++;

        _pData = (ulong*)NativeMemory.AllocZeroed((nuint)_lengthLongs * 8);

        // Copy elements from the old array.
        Buffer.MemoryCopy(pOldData, _pData, _lengthLongs * 8, Math.Min(prevLengthBytes, _lengthLongs * 8));

        NativeMemory.Free(pOldData);
    }

    /// <summary>Sets all elements of the existing array to false.</summary>
    /// <exception cref="ObjectDisposedException"></exception>
    public void Clear()
    {
        if (_disposedValue) throw new ObjectDisposedException(nameof(NativeBitArray));

        // Zero-out all of the data in the backing array.
        // We can do this in Int32.MaxValue chunks (is there a better method?)
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

    /// <summary>Disposes of native backing resources.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
