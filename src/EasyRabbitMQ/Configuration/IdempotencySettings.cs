namespace EasyRabbitMQ.Configuration;

public sealed class IdempotencySettings
{
    public bool Enabled { get; set; }

    public string HeaderName { get; set; } = "message-id";

    public int CacheTtlMinutes { get; set; } = 60;

    public int MaxTrackedMessageIds { get; set; } = 10000;
}
