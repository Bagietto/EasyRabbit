using System.Collections.Concurrent;
using EasyRabbitMQ.Configuration;

namespace EasyRabbitMQ.Idempotency;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _cacheByQueue = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CleanupState> _cleanupStateByQueue = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cleanupInterval;

    public InMemoryIdempotencyStore(TimeSpan? cleanupInterval = null)
    {
        _cleanupInterval = cleanupInterval.GetValueOrDefault(DefaultCleanupInterval);
        if (_cleanupInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupInterval), "Cleanup interval must be greater than zero.");
        }
    }

    public Task<bool> TryBeginProcessingAsync(
        string queueName,
        string messageId,
        IdempotencySettings settings,
        CancellationToken cancellationToken = default)
    {
        var queueCache = _cacheByQueue.GetOrAdd(queueName, _ => new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal));
        var cleanupState = _cleanupStateByQueue.GetOrAdd(queueName, _ => new CleanupState());

        var now = DateTimeOffset.UtcNow;
        TryCleanup(queueCache, cleanupState, settings, now, force: false);

        var expiresAt = now.AddMinutes(settings.CacheTtlMinutes);

        while (true)
        {
            if (queueCache.TryGetValue(messageId, out var currentExpiresAt) && currentExpiresAt > now)
            {
                return Task.FromResult(false);
            }

            if (queueCache.TryAdd(messageId, expiresAt))
            {
                if (queueCache.Count > settings.MaxTrackedMessageIds)
                {
                    TryCleanup(queueCache, cleanupState, settings, DateTimeOffset.UtcNow, force: true);
                }

                return Task.FromResult(true);
            }

            if (queueCache.TryGetValue(messageId, out var staleExpiresAt) && staleExpiresAt <= now)
            {
                if (queueCache.TryUpdate(messageId, expiresAt, staleExpiresAt))
                {
                    return Task.FromResult(true);
                }
            }
        }
    }

    public Task ReleaseAsync(string queueName, string messageId, CancellationToken cancellationToken = default)
    {
        if (_cacheByQueue.TryGetValue(queueName, out var queueCache))
        {
            queueCache.TryRemove(messageId, out _);
        }

        return Task.CompletedTask;
    }

    private void TryCleanup(
        ConcurrentDictionary<string, DateTimeOffset> queueCache,
        CleanupState cleanupState,
        IdempotencySettings settings,
        DateTimeOffset now,
        bool force)
    {
        if (!force)
        {
            var shouldRunPeriodicCleanup = queueCache.Count > 0 && now >= cleanupState.NextCleanupAt;
            if (!shouldRunPeriodicCleanup)
            {
                return;
            }
        }

        lock (cleanupState.Sync)
        {
            if (!force)
            {
                var shouldRunPeriodicCleanup = queueCache.Count > 0 && now >= cleanupState.NextCleanupAt;
                if (!shouldRunPeriodicCleanup)
                {
                    return;
                }
            }

            CleanupExpiredEntries(queueCache, now);
            CleanupOverflowEntries(queueCache, settings.MaxTrackedMessageIds);
            cleanupState.NextCleanupAt = DateTimeOffset.UtcNow.Add(_cleanupInterval);
        }
    }

    private static void CleanupExpiredEntries(ConcurrentDictionary<string, DateTimeOffset> queueCache, DateTimeOffset now)
    {
        foreach (var item in queueCache)
        {
            if (item.Value <= now)
            {
                queueCache.TryRemove(item.Key, out _);
            }
        }
    }

    private static void CleanupOverflowEntries(ConcurrentDictionary<string, DateTimeOffset> queueCache, int maxTrackedMessageIds)
    {
        var overflow = queueCache.Count - maxTrackedMessageIds;
        if (overflow <= 0)
        {
            return;
        }

        // Sort only during overflow cleanup to avoid steady-state allocation/cpu under high traffic.
        var candidates = queueCache.ToArray();
        Array.Sort(candidates, static (left, right) => left.Value.CompareTo(right.Value));

        for (var i = 0; i < overflow && i < candidates.Length; i++)
        {
            queueCache.TryRemove(candidates[i].Key, out _);
        }
    }

    private sealed class CleanupState
    {
        public object Sync { get; } = new();

        public DateTimeOffset NextCleanupAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
