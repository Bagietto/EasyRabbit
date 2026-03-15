using EasyRabbitMQ.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyRabbitMQ.Hosting;

public sealed class EasyRabbitConsumerHostedService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(200);

    private readonly IEasyRabbitRuntime _runtime;
    private readonly IReadOnlyList<IEasyRabbitMessageHandler> _handlers;
    private readonly ILogger<EasyRabbitConsumerHostedService> _logger;

    public EasyRabbitConsumerHostedService(
        IEasyRabbitRuntime runtime,
        IEnumerable<IEasyRabbitMessageHandler> handlers,
        ILogger<EasyRabbitConsumerHostedService> logger)
    {
        _runtime = runtime;
        _handlers = handlers.ToList();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _runtime.InitializeAsync(stoppingToken);

        if (_handlers.Count == 0)
        {
            _logger.LogWarning("EasyRabbitConsumerHostedService started without registered handlers.");
            return;
        }

        var workerTasks = _handlers.Select(handler => RunHandlerLoopAsync(handler, stoppingToken));
        await Task.WhenAll(workerTasks);
    }

    private async Task RunHandlerLoopAsync(IEasyRabbitMessageHandler handler, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runtime.RunConsumerLoopAsync(
                    handler.QueueName,
                    body => handler.HandleAsync(body, stoppingToken),
                    IdleDelay,
                    stoppingToken);

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in EasyRabbit consumer loop for queue '{QueueName}'.", handler.QueueName);
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
