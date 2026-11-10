using System.Collections.Concurrent;

namespace MiniLedger.Application.Services;

/// <summary>
/// Guarda respostas já processadas por chave de idempotência.
/// As entradas expiram (TTL padrão de 24h) para evitar crescimento indefinido da memória.
/// </summary>
public class IdempotencyStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, IdempotencyEntry> _store = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    public IdempotencyStore() : this(TimeProvider.System, DefaultTtl)
    {
    }

    public IdempotencyStore(TimeProvider timeProvider, TimeSpan ttl)
    {
        _timeProvider = timeProvider;
        _ttl = ttl;
    }

    public bool TryGet(string key, out IdempotencyEntry entry)
    {
        entry = null!;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!_store.TryGetValue(key, out var stored))
            return false;

        if (IsExpired(stored))
        {
            _store.TryRemove(key, out _);
            return false;
        }

        entry = stored;
        return true;
    }

    public void Save(string key, IdempotencyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        entry.CreatedAt = _timeProvider.GetUtcNow();
        _store[key] = entry;
        RemoveExpired();
    }

    private bool IsExpired(IdempotencyEntry entry)
        => _timeProvider.GetUtcNow() - entry.CreatedAt >= _ttl;

    private void RemoveExpired()
    {
        foreach (var pair in _store)
        {
            if (IsExpired(pair.Value))
                _store.TryRemove(pair.Key, out _);
        }
    }
}

public class IdempotencyEntry
{
    public int StatusCode { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
