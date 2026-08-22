namespace PigComic.Core.Imaging;

/// <summary>
/// Byte-budgeted LRU cache used for tile accounting (SPEC §20: LRU by (pageId,
/// TileKey), byte budget default 384 MB, eviction on insert). Values are
/// arbitrary (SKImage in the App); size comes from a caller-supplied function.
/// Thread-safe.
/// </summary>
public sealed class LruByteCache<TKey, TValue>
    where TKey : notnull
{
    private readonly long _budgetBytes;
    private readonly Func<TValue, long> _sizeOf;
    private readonly Action<TKey, TValue>? _onEvict;

    private readonly Dictionary<TKey, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _lru = new();
    private long _totalBytes;

    private sealed class Entry(TKey key, TValue value, long size)
    {
        public TKey Key { get; } = key;
        public TValue Value { get; } = value;
        public long Size { get; } = size;
    }

    public LruByteCache(long budgetBytes, Func<TValue, long> sizeOf, Action<TKey, TValue>? onEvict = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        _budgetBytes = budgetBytes;
        _sizeOf = sizeOf;
        _onEvict = onEvict;
    }

    public long BudgetBytes => _budgetBytes;

    public long UsedBytes
    {
        get { lock (_lru) { return _totalBytes; } }
    }

    public int Count
    {
        get { lock (_lru) { return _index.Count; } }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lru)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddLast(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    /// <summary>Inserts (or refreshes) an item; evicts least-recently-used items to stay within budget.</summary>
    public void Insert(TKey key, TValue value)
    {
        lock (_lru)
        {
            if (_index.TryGetValue(key, out var existing))
            {
                _totalBytes -= existing.Value.Size;
                _lru.Remove(existing);
                _index.Remove(key);
            }

            var size = Math.Max(0, _sizeOf(value));
            var entry = new Entry(key, value, size);
            _index[key] = _lru.AddLast(entry);
            _totalBytes += size;

            while (_totalBytes > _budgetBytes && _lru.Count > 1 && _lru.First is { } first)
            {
                _lru.RemoveFirst();
                _index.Remove(first.Value.Key);
                _totalBytes -= first.Value.Size;
                _onEvict?.Invoke(first.Value.Key, first.Value.Value);
            }
        }
    }

    public void Clear()
    {
        lock (_lru)
        {
            while (_lru.First is { } first)
            {
                _lru.RemoveFirst();
                _index.Remove(first.Value.Key);
                _totalBytes -= first.Value.Size;
                _onEvict?.Invoke(first.Value.Key, first.Value.Value);
            }
        }
    }
}