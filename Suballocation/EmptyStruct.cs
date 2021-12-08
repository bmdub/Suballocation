
namespace Suballocation;

/// <summary>
/// Simply used in place of generic types where no instance is needed.
/// </summary>
public struct EmptyStruct
{
    // Note: This still consumes 1 byte, because no type can be 0-length.
}
