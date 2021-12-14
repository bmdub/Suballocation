using Suballocation.Suballocators;

namespace Suballocation.Trackers;

/// <summary>
/// Light-weight structure that describes a portion of unmanaged memory within a suballocator.
/// Note that this class is unsafe, and most forms of validation are intentionally omitted. Use at your own risk.
/// </summary>
[DebuggerDisplay("[0x{(ulong)_windowPtr}] Length: {_length}")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe readonly record struct UpdateWindow<T> : IEnumerable<T>, IUpdateWindow where T : unmanaged
{
    private readonly IntPtr _bufferPtr;
    private readonly IntPtr _windowPtr;
    private readonly long _length;

    /// <summary></summary>
    /// <param name="bufferPtr">A pointer to the start of the containing memory buffer.</param>
    /// <param name="windowPtr">A pointer to the start of the window.</param>
    /// <param name="length">The unit length of the segment.</param>
    public unsafe UpdateWindow(T* bufferPtr, T* windowPtr, long length)
    {
        // Using pointers instead of references to avoid GC overhead.
        _bufferPtr = (IntPtr)bufferPtr;
        _windowPtr = (IntPtr)windowPtr;
        _length = length;
    }

    /// <summary>Pointer to the start of the buffer that contains this window.</summary>
    public T* PBuffer { get => (T*)_bufferPtr; init => _bufferPtr = (IntPtr)value; }

    /// <summary>Pointer to the start of the pinned window in unmanaged memory.</summary>
    public unsafe T* PSegment { get => (T*)_windowPtr; init => _windowPtr = (IntPtr)value; }

    /// <summary>The total unit length of the window.</summary>
    public long Length { get => _length; init => _length = value; }

    /// <summary>A reference to the ith element of the window.</summary>
    public ref T this[long index] => ref ((T*)_windowPtr)[index];

    /// <summary>The suballocator where this window resides.</summary>
    public ISuballocator<T>? Suballocator
    {
        get
        {
            if (SuballocatorTable<T>.TryGetByBufferAddress(_bufferPtr, out var suballocator) == false)
            {
                return null;
            }

            return suballocator;
        }
    }

    public void* WindowBytesPtr { get => (void*)_windowPtr; }

    public void* BufferBytesPtr { get => (void*)_bufferPtr; }

    public long LengthBytes => _length * Unsafe.SizeOf<T>();

    ISuballocator IUpdateWindow.Suballocator => Suballocator!;

    public long RangeOffset => ((long)_windowPtr - (long)_bufferPtr) / Unsafe.SizeOf<T>();

    public long RangeLength => Length;

    /// <summary>A Span<typeparamref name="T"/> on top of the segment.</summary>
    public Span<T> AsSpan()
    {
        if (_length > int.MaxValue) throw new InvalidOperationException($"Unable to return a Span<T> for a range that is larger than int.Maxvalue.");

        return new Span<T>((T*)_windowPtr, (int)_length);
    }

    public override string ToString() =>
        $"[0x{(ulong)_windowPtr}] Length: {_length:N0}, Value: {this[0]}";

    public IEnumerator<T> GetEnumerator()
    {
        for (long i = 0; i < _length; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
