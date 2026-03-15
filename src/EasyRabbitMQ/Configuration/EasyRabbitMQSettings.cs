namespace EasyRabbitMQ.Configuration;

public sealed class EasyRabbitMQSettings
{
    public ConnectionSettings Connection { get; set; } = new();

    public IReadOnlyList<QueueSettings> Queues { get; set; } = Array.Empty<QueueSettings>();
}
