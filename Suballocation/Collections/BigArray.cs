
namespace Suballocation.Collections;

/// <summary>Collection that simulates an array that is virtually unbounded in length/size.</summary>
/// <typeparam name="T">The element type.</typeparam>
public class BigArray<T>
{
    private const int _maxSubarraySize = 1 << 28;
    private readonly T[][] _arrays;
    private readonly int _maxElementsPerArray;

    /// <summary></summary>
    /// <param name="length">The fixed number of elements in the array.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public BigArray(long length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        _maxElementsPerArray = _maxSubarraySize / Unsafe.SizeOf<T>();

        int arrayCt = (int)(length / _maxElementsPerArray);

        if (arrayCt * _maxElementsPerArray != length)
        {
            arrayCt++;
        }

        _arrays = new T[arrayCt][];

        for (int i = 0; length > 0; i++)
        {
            int partLength = (int)Math.Min(_maxElementsPerArray, length);

            _arrays[i] = new T[partLength];

            length -= partLength;
        }

        Length = length;
    }

    /// <summary>The length of the array.</summary>
    public long Length { get; init; }

    /// <summary>References an element in the array.</summary>
    /// <param name="index">The array index of the desired element.</param>
    /// <returns>A reference to the element at the specified index.</returns>
    public ref T this[long index]
    {
        get
        {
            var arrIndex = index / _maxElementsPerArray;
            var elemIndex = index - (arrIndex * _maxElementsPerArray);

            return ref _arrays[index / _maxElementsPerArray][elemIndex];
        }
    }

    /// <summary>Clears the array elements to an initial state.</summary>
    public void Clear()
    {
        for(int i=0; i<_arrays.Length; i++)
        {
            _arrays[i] = new T[_arrays[i].Length];
        }
    }
}
