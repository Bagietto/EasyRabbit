using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EasyRabbitMQ.Observability;

internal static class EasyRabbitTelemetry
{
    public static readonly ActivitySource ActivitySource = new("EasyRabbitMQ.Runtime");

    private static readonly Meter Meter = new("EasyRabbitMQ.Runtime", "1.0.0");

    public static readonly Counter<long> PublishedCounter = Meter.CreateCounter<long>("easyrabbit.messages.published");
    public static readonly Counter<long> ProcessedCounter = Meter.CreateCounter<long>("easyrabbit.messages.processed");
    public static readonly Counter<long> RetriedCounter = Meter.CreateCounter<long>("easyrabbit.messages.retried");
    public static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>("easyrabbit.messages.deadletter");
    public static readonly Counter<long> DuplicateCounter = Meter.CreateCounter<long>("easyrabbit.messages.duplicate_ignored");
    public static readonly Counter<long> ReplayedCounter = Meter.CreateCounter<long>("easyrabbit.messages.replayed");
    public static readonly Counter<long> CircuitOpenCounter = Meter.CreateCounter<long>("easyrabbit.circuit.open");
    public static readonly Counter<long> RecoveryCounter = Meter.CreateCounter<long>("easyrabbit.connection.recovery");
    public static readonly Counter<long> ErrorCounter = Meter.CreateCounter<long>("easyrabbit.runtime.errors");
}

