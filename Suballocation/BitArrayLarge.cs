using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ztest
{
	internal unsafe sealed class BitArrayLarge : IDisposable
	{
		public readonly ulong Length;
		public readonly ulong LengthBytes;
		private const ulong BitOffsetMask = 0x07;
		private readonly byte* _pData;
		private bool _disposedValue;

		public BitArrayLarge(ulong length)
		{
			if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be 0.");

			Length = length;
			LengthBytes = length / 8;
			if ((length & BitOffsetMask) != 0) LengthBytes++;

			_pData = (byte*)NativeMemory.AllocZeroed((nuint)LengthBytes);
		}

		public bool this[ulong index]
		{
			get
			{
				ulong byteIndex = index >> 3;
				var bitMask = 1u << unchecked((int)(index & BitOffsetMask));

				return (_pData[byteIndex] & bitMask) == bitMask;
			}
			set
			{
				ulong byteIndex = index >> 3;
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

		~BitArrayLarge()
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
