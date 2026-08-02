using System.Collections.Generic;

namespace Rewind.Sdk;

internal sealed class BoundedQueue<T>
{
    private readonly Queue<T> _items;
    private readonly int _capacity;

    public BoundedQueue(int capacity)
    {
        _capacity = capacity;
        _items = new Queue<T>(capacity);
    }

    public bool TryEnqueue(T value)
    {
        lock (_items)
        {
            if (_items.Count >= _capacity)
            {
                return false;
            }

            _items.Enqueue(value);
            return true;
        }
    }

    public bool TryDequeue(out T? value)
    {
        lock (_items)
        {
            if (_items.Count == 0)
            {
                value = default;
                return false;
            }

            value = _items.Dequeue();
            return true;
        }
    }

    public bool TryPeek(out T? value)
    {
        lock (_items)
        {
            if (_items.Count == 0)
            {
                value = default;
                return false;
            }

            value = _items.Peek();
            return true;
        }
    }

    public int Count
    {
        get
        {
            lock (_items)
            {
                return _items.Count;
            }
        }
    }
}
