
namespace Suballocation;

public interface ISegmentResource : IDisposable
{
    ISuballocator MemoryPool { get; }
}
