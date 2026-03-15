namespace EasyRabbitMQ.Configuration;

public sealed class QueueSettings
{
    public string Name { get; set; } = string.Empty;

    public bool Durable { get; set; } = true;

    public bool AutoDelete { get; set; }

    public ushort PrefetchCount { get; set; } = 10;

    public int Workers { get; set; } = 1;

    public RetrySettings Retry { get; set; } = new();

    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();

    public IdempotencySettings Idempotency { get; set; } = new();
}

