
namespace Suballocation.Collections;
/*
public unsafe class RedBlackTree<TValue>
{
    private Node _root;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private class Node
    {
        private long _key;
        private TValue _value;
        
        public long Key { get => _key; init => _key = value; }
        public TValue Value { get => _value; init => _value = value; }
    }

    public RedBlackTree()
    {
    }

    public long Count { get; private set; }

    public void Add(long key, TValue value)
    {
        if (TryAdd(key, value) == false)
        {
            throw new ArgumentException($"Key already exists in the collection: {key}");
        }
    }

    public bool TryAdd(long key, TValue value)
    {
    }

    public TValue this[long key]
    {
        get
        {
            if (TryGetValue(key, out var value) == false)
            {
                throw new KeyNotFoundException();
            }

            return value;
        }
        set
        {
        }
    }

    public bool TryGetValue(long key, out TValue value)
    {
    }

    public bool TryRemove(long key, out TValue value)
    {
    }

    public void Clear()
    {
    }
}
*/