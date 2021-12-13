using Suballocation.Suballocators;

namespace Suballocation.Trackers;

/// <summary>
/// Exposes basic information about a portion of a suballocator's buffer.
/// </summary>
public interface IUpdateWindow
{
    /// <summary>Pointer to the start of the buffer that contains this window.</summary>
    unsafe void* PBufferBytes { get; }

    /// <summary>Pointer to the start of the pinned window in unmanaged memory.</summary>
    unsafe void* PWindowBytes { get; }

    /// <summary>The total size of the window.</summary>
    long LengthBytes { get; }

    /// <summary>The suballocator where this window resides.</summary>
    ISuballocator Suballocator { get; }
}
