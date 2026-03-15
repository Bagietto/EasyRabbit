using EasyRabbitMQ.Configuration;
using StackExchange.Redis;

namespace EasyRabbitMQ.Idempotency;

public sealed class RedisIdempotencyStore : IIdempotencyStore, IDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _redisConnectionFactory;
    private readonly string _keyPrefix;

    public RedisIdempotencyStore(RedisIdempotencyStoreOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("Redis connection string is required.", nameof(options.ConnectionString));
        }

        _redisConnectionFactory = new Lazy<Task<ConnectionMultiplexer>>(
            () => ConnectionMultiplexer.ConnectAsync(options.ConnectionString),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _keyPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix) ? "easyrabbit:idempotency" : options.KeyPrefix;
    }

    public async Task<bool> TryBeginProcessingAsync(
        string queueName,
        string messageId,
        IdempotencySettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = await GetDatabaseAsync(cancellationToken);
        var key = BuildKey(queueName, messageId);
        var ttl = TimeSpan.FromMinutes(settings.CacheTtlMinutes);

        return await db.StringSetAsync(key, "1", expiry: ttl, when: When.NotExists);
    }

    public async Task ReleaseAsync(
        string queueName,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = await GetDatabaseAsync(cancellationToken);
        await db.KeyDeleteAsync(BuildKey(queueName, messageId));
    }

    public void Dispose()
    {
        if (!_redisConnectionFactory.IsValueCreated)
        {
            return;
        }

        var connectionTask = _redisConnectionFactory.Value;
        if (!connectionTask.IsCompletedSuccessfully)
        {
            return;
        }

        connectionTask.Result.Dispose();
    }

    private async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await _redisConnectionFactory.Value.WaitAsync(cancellationToken);
        return multiplexer.GetDatabase();
    }

    private string BuildKey(string queueName, string messageId)
    {
        return $"{_keyPrefix}:{queueName}:{messageId}";
    }
}
