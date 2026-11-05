using System.Diagnostics.CodeAnalysis;

namespace KnowledgeAgent.Features.Prompts;

internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _lock = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value = new Entry(key, value);
                _order.Remove(existing);
                _order.AddFirst(existing);
                return;
            }

            _map[key] = _order.AddFirst(new Entry(key, value));
            if (_map.Count > _capacity && _order.Last is { } oldest)
            {
                _order.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }
        }
    }

    public int RemoveWhere(Func<TKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_lock)
        {
            var keys = _map.Keys.Where(predicate).ToList();
            foreach (var key in keys)
            {
                _order.Remove(_map[key]);
                _map.Remove(key);
            }

            return keys.Count;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
