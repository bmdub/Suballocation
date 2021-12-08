
namespace Suballocation.Collections;

public class BigArray<T>
{
    private const int _maxSubarraySize = 1 << 28;
    private readonly T[][] _arrays;
    private readonly int _maxElementsPerArray;

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

    public long Length { get; init; }

    public ref T this[long index]
    {
        get
        {
            var arrIndex = index / _maxElementsPerArray;
            var elemIndex = index - (arrIndex * _maxElementsPerArray);

            return ref _arrays[index / _maxElementsPerArray][elemIndex];
        }
    }

    public void Clear()
    {
        for(int i=0; i<_arrays.Length; i++)
        {
            _arrays[i] = new T[_arrays[i].Length];
        }

        /*foreach(var arr in _arrays)
        {
            arr.AsSpan().Clear();
        }*/
    }
}
