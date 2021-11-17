using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Suballocation;

[DebuggerDisplay("[0x{(ulong)_ptr}] Length: {_length}, Size: {Size}, Value: {this[0]}")]
[StructLayout(LayoutKind.Sequential)]
public unsafe readonly record struct NativeMemorySegment<T> : ISegment<T> where T : unmanaged
{
    private readonly IntPtr _ptr;
    private readonly long _size;

    public unsafe NativeMemorySegment(T* ptr, long length)
    {
        _ptr = (IntPtr)ptr;
        _size = length;
    }

    public unsafe void* PBytes { get => (void*)_ptr; init => _ptr = (IntPtr)value; }

    public long LengthBytes => _size * Unsafe.SizeOf<T>();

    public unsafe T* PElems { get => (T*)_ptr; init => _ptr = (IntPtr)value; }

    public long Length { get => _size; init => _size = value; }

    public ref T Value => ref *(T*)_ptr;

    public ref T this[long index] => ref ((T*)_ptr)[index];

    public Span<T> AsSpan()
    {
        if (_size > int.MaxValue) throw new InvalidOperationException($"Unable to return a Span<T> for a range that is larger than int.Maxvalue.");

        return new Span<T>((T*)_ptr, (int)_size);
    }

    public override string ToString() =>
        $"[0x{(ulong)_ptr}] Length: {_size:N0}, Size: {LengthBytes:N0}, Value: {this[0]}";

    public IEnumerator<T> GetEnumerator()
    {
        for (long i = 0; i < _size; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
