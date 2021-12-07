using Suballocation.Suballocators;

namespace Suballocation;

/// <summary>
/// Lightweight structure that represents a segment of unmanaged memory allocated from a suballocator.
/// Note that this class is unsafe, and most forms of validation are intentionally omitted. Use at your own risk.
/// </summary>
[DebuggerDisplay("[0x{(ulong)_ptr}] Length: {_length}, Value: {this[0]}")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe readonly record struct NativeMemorySegment<T> : ISegment, IEnumerable<T> where T : unmanaged
{
    private readonly IntPtr _ptr;
    private readonly long _length;
    private readonly uint _suballocatorId;

    /// <summary></summary>
    /// <param name="suballocatorHandlePtr">An ID belonging to a registered ISuballocator<typeparamref name="T"/> instance used to allocatee this segment.</param>
    /// <param name="ptr">A pointer to the start of the memory segment in unmanaged memory.</param>
    /// <param name="length">The unit length of the segment.</param>
    public unsafe NativeMemorySegment(uint suballocatorId, T* ptr, long length)
    {
        // Using pointers instead of references to avoid GC overhead.
        _suballocatorId = suballocatorId;
        _ptr = (IntPtr)ptr;
        _length = length;
    }

    /// <summary>Pointer to the start of the pinned segment in unmanaged memory.</summary>
    public unsafe T* PElems { get => (T*)_ptr; init => _ptr = (IntPtr)value; }

    /// <summary>The total unit length of segment.</summary>
    public long Length { get => _length; init => _length = value; }

    /// <summary>A reference to the first or only value of the segment.</summary>
    public ref T Value => ref *(T*)_ptr;

    /// <summary>A reference to the ith element of the segment.</summary>
    public ref T this[long index] => ref ((T*)_ptr)[index];

    /// <summary>The suballocator that allocated this segment, or Null if not found or disposed.</summary>
    public ISuballocator<T>? Suballocator 
    { 
        get        
        {
            if(SuballocatorTable<T>.TryGetByID(_suballocatorId, out var suballocator) == false)
            {
                return null;
            }

            return suballocator;
        }
    }

    public unsafe void* PBytes { get => (T*)_ptr; }

    public long LengthBytes => _length * Unsafe.SizeOf<T>();

    ISuballocator ISegment.Suballocator => Suballocator!;

    /// <summary>A Span<typeparamref name="T"/> on top of the segment.</summary>
    public Span<T> AsSpan()
    {
        if (_length > int.MaxValue) throw new InvalidOperationException($"Unable to return a Span<T> for a range that is larger than int.Maxvalue.");

        return new Span<T>((T*)_ptr, (int)_length);
    }

    public override string ToString() =>
        $"[0x{(ulong)_ptr}] Length: {_length:N0}, Value: {this[0]}";

    public IEnumerator<T> GetEnumerator()
    {
        for (long i = 0; i < _length; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();

    public void Dispose()
    {
        Suballocator?.Return(this);
    }
}
