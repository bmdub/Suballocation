
namespace Suballocation;

/// <summary>
/// Exposes type-agnostic update window tracker methodsd.
/// </summary>
public interface IUpdateWindowTracker
{
    /// <summary>Builds the final set of optimized/combined update windows based on the registered segment updates.</summary>
    /// <returns></returns>
    IUpdateWindows BuildUpdateWindows();

    /// <summary>Clears the tracker of all state, so it can be reused.</summary>
    void Clear();
}
