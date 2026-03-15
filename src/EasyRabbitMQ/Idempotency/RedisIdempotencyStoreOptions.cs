namespace EasyRabbitMQ.Idempotency;

public sealed class RedisIdempotencyStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string KeyPrefix { get; set; } = "easyrabbit:idempotency";
}
