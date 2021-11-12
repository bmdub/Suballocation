
namespace Suballocation;

public interface ISegment
{
    unsafe void* PBytes { get; }
    public long Size { get; }
}

public interface ISegment<T> : ISegment, IEnumerable<T> where T : unmanaged
{
    unsafe T* PElems { get; }
    long Length { get; }
    ref T Value { get; }
    ref T this[long index] { get; }
    Span<T> AsSpan();
}
