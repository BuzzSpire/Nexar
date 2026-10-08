namespace Nexar;

/// <summary>
/// An in-memory <see cref="IHttpCache"/> bounded by total size, evicting the least recently used entries.
/// </summary>
public sealed class MemoryHttpCache : IHttpCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, CachedResponse Entry)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, CachedResponse Entry)> _recency = new();
    private long _size;

    /// <summary>
    /// Creates a cache holding up to <paramref name="maxSizeBytes"/> (default 50 MB), with bodies of at most
    /// <paramref name="maxEntryBytes"/> each (default 5 MB).
    /// </summary>
    public MemoryHttpCache(long maxSizeBytes = 50 * 1024 * 1024, long maxEntryBytes = 5 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntryBytes);
        MaxSizeBytes = maxSizeBytes;
        MaxEntryBytes = Math.Min(maxEntryBytes, maxSizeBytes);
    }

    /// <summary>The total size the cache may use.</summary>
    public long MaxSizeBytes { get; }

    /// <inheritdoc />
    public long MaxEntryBytes { get; }

    /// <summary>The number of stored entries.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>The estimated size of all entries.</summary>
    public long SizeBytes
    {
        get
        {
            lock (_gate)
            {
                return _size;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<CachedResponse?> GetAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                return ValueTask.FromResult<CachedResponse?>(null);
            }
            _recency.Remove(node);
            _recency.AddFirst(node);
            return ValueTask.FromResult<CachedResponse?>(node.Value.Entry);
        }
    }

    /// <inheritdoc />
    public ValueTask SetAsync(string key, CachedResponse entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            RemoveLocked(key);
            if (entry.Size > MaxSizeBytes)
            {
                return ValueTask.CompletedTask;
            }
            _entries[key] = _recency.AddFirst((key, entry));
            _size += entry.Size;
            while (_size > MaxSizeBytes && _recency.Last is { } oldest)
            {
                RemoveLocked(oldest.Value.Key);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            RemoveLocked(key);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            _size = 0;
        }
    }

    private void RemoveLocked(string key)
    {
        if (_entries.Remove(key, out var node))
        {
            _recency.Remove(node);
            _size -= node.Value.Entry.Size;
        }
    }
}
