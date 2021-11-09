using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Suballocation
{
    internal unsafe sealed class NativeBitArray : IDisposable
    {
        public readonly long Length;
        public readonly long LengthBytes;
        private const long BitOffsetMask = 0x07;
        private readonly byte* _pData;
        private bool _disposedValue;

        public NativeBitArray(long length)
        {
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be 0.");

            Length = length;
            LengthBytes = length / 8;
            if ((length & BitOffsetMask) != 0) LengthBytes++;

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
                    _pData[byteIndex] = unchecked((byte)bitMask);
                }
                else
                {
                    _pData[byteIndex] &= unchecked((byte)~bitMask);
                }
            }
        }

        public void Clear()
        {
            for (long i = 0; i < LengthBytes; i += uint.MaxValue)
            {
                uint length = (uint)Math.Min(uint.MaxValue, LengthBytes - i);

                Unsafe.InitBlock(_pData, 0, length);
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
}
