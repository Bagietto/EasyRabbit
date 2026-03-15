namespace EasyRabbitMQ.Configuration;

public sealed class RetrySettings
{
    public RetryMode Mode { get; set; } = RetryMode.Exponential;

    public int MaxAttempts { get; set; } = 5;

    public int? FixedDelayMs { get; set; }

    public int? InitialDelayMs { get; set; } = 1000;

    public int? MaxDelayMs { get; set; } = 60000;

    public double? Multiplier { get; set; } = 2.0;
}
