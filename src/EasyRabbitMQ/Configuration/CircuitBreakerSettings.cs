namespace EasyRabbitMQ.Configuration;

public sealed class CircuitBreakerSettings
{
    public bool Enabled { get; set; } = true;

    public int FailureThreshold { get; set; } = 10;

    public int BreakDurationSeconds { get; set; } = 30;
}
