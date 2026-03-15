using EasyRabbitMQ.Configuration;
using EasyRabbitMQ.Runtime;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyRabbitMQ.HealthChecks;

public sealed class EasyRabbitHealthCheck : IHealthCheck
{
    private readonly IEasyRabbitRuntime _runtime;
    private readonly EasyRabbitMQSettings _settings;

    public EasyRabbitHealthCheck(IEasyRabbitRuntime runtime, EasyRabbitMQSettings settings)
    {
        _runtime = runtime;
        _settings = settings;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _runtime.InitializeAsync(cancellationToken);

            foreach (var queue in _settings.Queues)
            {
                var topology = _runtime.GetQueueTopology(queue.Name);
                _ = await _runtime.GetQueueMessageCountAsync(topology.MainQueue, cancellationToken);
            }

            return HealthCheckResult.Healthy("EasyRabbit runtime is reachable and topology is available.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("EasyRabbit runtime is unavailable.", ex);
        }
    }
}
