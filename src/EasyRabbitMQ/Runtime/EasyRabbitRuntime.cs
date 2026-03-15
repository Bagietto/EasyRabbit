using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using EasyRabbitMQ.Configuration;
using EasyRabbitMQ.Idempotency;
using EasyRabbitMQ.Observability;
using EasyRabbitMQ.Resilience;
using EasyRabbitMQ.Topology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace EasyRabbitMQ.Runtime;

public sealed class EasyRabbitRuntime : IEasyRabbitRuntime
{
    private const string AttemptHeaderName = "x-easyrabbit-attempt";

    private readonly EasyRabbitMQSettings _settings;
    private readonly ConcurrentDictionary<string, QueueSettings> _queueSettingsMap;
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakerMap;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly Func<ConnectionSettings> _connectionSettingsProvider;
    private readonly ILogger<EasyRabbitRuntime> _logger;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);

    private IConnection? _connection;
    private bool _initialized;
    private readonly ConcurrentBag<IChannel> _publishChannelPool = new();
    private readonly int _maxPublishChannelPoolSize;
    private int _publishIdleChannels;

    public EasyRabbitRuntime(
        EasyRabbitMQSettings settings,
        Func<ConnectionSettings>? connectionSettingsProvider = null,
        IIdempotencyStore? idempotencyStore = null,
        ILogger<EasyRabbitRuntime>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionSettingsProvider = connectionSettingsProvider ?? (() => _settings.Connection);
        _idempotencyStore = idempotencyStore ?? new InMemoryIdempotencyStore();
        _logger = logger ?? NullLogger<EasyRabbitRuntime>.Instance;

        EasyRabbitMQSettingsValidator.Validate(_settings);
        _maxPublishChannelPoolSize = _settings.Connection.PublishChannelPoolSize;

        _queueSettingsMap = new ConcurrentDictionary<string, QueueSettings>(StringComparer.OrdinalIgnoreCase);
        _circuitBreakerMap = new ConcurrentDictionary<string, CircuitBreakerState>(StringComparer.OrdinalIgnoreCase);

        foreach (var queue in _settings.Queues)
        {
            _queueSettingsMap[queue.Name] = queue;
            _circuitBreakerMap[queue.Name] = new CircuitBreakerState();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = EasyRabbitTelemetry.ActivitySource.StartActivity("runtime.initialize", ActivityKind.Internal);

        if (_initialized && IsConnectionHealthy())
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && IsConnectionHealthy())
            {
                return;
            }

            await RecreateConnectionCoreAsync(cancellationToken);
            _initialized = true;

            _logger.LogInformation("EasyRabbit runtime initialized with {QueueCount} queues.", _settings.Queues.Count);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public Task PublishAsync(
        string queueName,
        ReadOnlyMemory<byte> body,
        bool persistent = true,
        string? messageId = null,
        IDictionary<string, object?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        _ = GetQueueSettings(queueName);

        return ExecuteWithRecoveryOnConnectionAsync(
            async connection =>
            {
                var publishChannel = await RentPublishChannelAsync(connection, cancellationToken);
                try
                {
                    using var activity = EasyRabbitTelemetry.ActivitySource.StartActivity("message.publish", ActivityKind.Producer);
                    activity?.SetTag("messaging.destination.name", queueName);

                    var mergedHeaders = headers is null
                        ? new Dictionary<string, object?>(StringComparer.Ordinal)
                        : new Dictionary<string, object?>(headers, StringComparer.Ordinal);

                    mergedHeaders[AttemptHeaderName] = 1;

                    var properties = new BasicProperties
                    {
                        Persistent = persistent,
                        MessageId = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId,
                        Headers = mergedHeaders
                    };

                    await publishChannel.BasicPublishAsync(
                        exchange: GetMainExchangeName(queueName),
                        routingKey: queueName,
                        mandatory: false,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: cancellationToken);

                    EasyRabbitTelemetry.PublishedCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
                    _logger.LogDebug("Message published to queue '{QueueName}'.", queueName);
                }
                finally
                {
                    await ReturnPublishChannelAsync(publishChannel);
                }

                return true;
            },
            cancellationToken);
    }

    public Task<MessageProcessingResult> ConsumeOnceAsync(
        string queueName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(handler);

        return ExecuteWithRecoveryAsync(
            channel => ConsumeOnceCoreAsync(channel, queueName, handler, cancellationToken),
            cancellationToken);
    }

    public async Task RunConsumerLoopAsync(
        string queueName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        TimeSpan idleDelay,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(handler);

        if (idleDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleDelay), "Idle delay cannot be negative.");
        }

        await InitializeAsync(cancellationToken);

        var queueConfig = GetQueueSettings(queueName);

        while (!cancellationToken.IsCancellationRequested)
        {
            IChannel? workerChannel = null;
            try
            {
                var connection = _connection ?? throw new EasyRabbitConfigurationException("Connection is not initialized.");
                workerChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await workerChannel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: queueConfig.PrefetchCount,
                    global: false,
                    cancellationToken: cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await ConsumeOnceCoreAsync(workerChannel, queueName, handler, cancellationToken);
                    if (result is MessageProcessingResult.NoMessage or MessageProcessingResult.CircuitOpen)
                    {
                        await Task.Delay(idleDelay, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsRecoverable(ex) && _settings.Connection.AutomaticRecoveryEnabled)
            {
                EasyRabbitTelemetry.RecoveryCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
                _logger.LogWarning(ex, "Recoverable error in consumer loop for queue '{QueueName}'. Reinitializing runtime.", queueName);

                await RecoverConnectionAsync(cancellationToken);
                await Task.Delay(200, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled consumer loop error for queue '{QueueName}'.", queueName);
                await Task.Delay(idleDelay, cancellationToken);
            }
            finally
            {
                if (workerChannel is not null)
                {
                    await workerChannel.DisposeAsync();
                }
            }
        }
    }

    public async Task<int> ConsumeManyAsync(
        string queueName,
        int maxMessages,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(handler);

        if (maxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "maxMessages must be greater than zero.");
        }

        await InitializeAsync(cancellationToken);

        var queueConfig = GetQueueSettings(queueName);
        var workers = Math.Max(1, Math.Min(queueConfig.Workers, maxMessages));
        var budget = new ConsumeBudget(maxMessages);

        _logger.LogInformation("Starting ConsumeMany for queue '{QueueName}' with {Workers} workers and prefetch {Prefetch}.", queueName, workers, queueConfig.PrefetchCount);

        var workerTasks = new List<Task<int>>(workers);

        for (var i = 0; i < workers; i++)
        {
            workerTasks.Add(Task.Run(
                async () => await ConsumeManyWorkerAsync(queueName, handler, queueConfig.PrefetchCount, budget, cancellationToken),
                cancellationToken));
        }

        var results = await Task.WhenAll(workerTasks);
        return results.Sum();
    }

    public async Task<int> ReplayDeadLetterAsync(
        string queueName,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        if (maxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "maxMessages must be greater than zero.");
        }

        var topology = TopologyBuilder.Build(queueName);
        var replayed = 0;

        for (var i = 0; i < maxMessages; i++)
        {
            var replayedCurrent = await ExecuteWithRecoveryAsync(
                async channel =>
                {
                    var deadMessage = await channel.BasicGetAsync(topology.DeadLetterQueue, autoAck: false, cancellationToken: cancellationToken);
                    if (deadMessage is null)
                    {
                        return false;
                    }

                    var headers = CloneHeaders(deadMessage.BasicProperties?.Headers);
                    headers[AttemptHeaderName] = 1;
                    headers["x-easyrabbit-replay-count"] = ConvertToInt32(headers.TryGetValue("x-easyrabbit-replay-count", out var replayCount) ? replayCount : 0) + 1;

                    var props = new BasicProperties
                    {
                        Persistent = true,
                        MessageId = deadMessage.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("N"),
                        Headers = headers
                    };

                    await channel.BasicPublishAsync(
                        exchange: GetMainExchangeName(queueName),
                        routingKey: topology.MainQueue,
                        mandatory: false,
                        basicProperties: props,
                        body: deadMessage.Body,
                        cancellationToken: cancellationToken);

                    await channel.BasicAckAsync(deadMessage.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                    return true;
                },
                cancellationToken);

            if (!replayedCurrent)
            {
                break;
            }

            replayed++;
            EasyRabbitTelemetry.ReplayedCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
        }

        return replayed;
    }
    public Task<uint> GetQueueMessageCountAsync(string queueName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        return ExecuteWithRecoveryAsync(
            async channel =>
            {
                var queueInfo = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken: cancellationToken);
                return queueInfo.MessageCount;
            },
            cancellationToken);
    }

    public QueueTopology GetQueueTopology(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        _ = GetQueueSettings(queueName);
        return TopologyBuilder.Build(queueName);
    }

    public async ValueTask DisposeAsync()
    {
        await _initializeLock.WaitAsync();
        try
        {
            await DisposePublishChannelPoolAsync();

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _initialized = false;
            _logger.LogInformation("EasyRabbit runtime disposed.");
        }
        finally
        {
            _initializeLock.Release();
            _initializeLock.Dispose();
        }
    }

    private async Task<MessageProcessingResult> ConsumeOnceCoreAsync(
        IChannel channel,
        string queueName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken)
    {
        using var activity = EasyRabbitTelemetry.ActivitySource.StartActivity("message.consume", ActivityKind.Consumer);
        activity?.SetTag("messaging.destination.name", queueName);

        var queueConfig = GetQueueSettings(queueName);
        var topology = TopologyBuilder.Build(queueName);

        if (IsCircuitOpen(queueName, queueConfig))
        {
            EasyRabbitTelemetry.CircuitOpenCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
            _logger.LogWarning("Circuit is open for queue '{QueueName}'.", queueName);
            return MessageProcessingResult.CircuitOpen;
        }

        var message = await channel.BasicGetAsync(queue: topology.MainQueue, autoAck: false, cancellationToken: cancellationToken);
        if (message is null)
        {
            return MessageProcessingResult.NoMessage;
        }

        var idempotencyMessageId = ResolveMessageId(queueConfig, message.BasicProperties);
        var idempotencyTokenAcquired = false;

        if (queueConfig.Idempotency.Enabled && !string.IsNullOrWhiteSpace(idempotencyMessageId))
        {
            idempotencyTokenAcquired = await _idempotencyStore.TryBeginProcessingAsync(
                queueName,
                idempotencyMessageId!,
                queueConfig.Idempotency,
                cancellationToken);

            if (!idempotencyTokenAcquired)
            {
                await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                EasyRabbitTelemetry.DuplicateCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
                _logger.LogInformation("Duplicate message ignored for queue '{QueueName}' and MessageId '{MessageId}'.", queueName, idempotencyMessageId);
                return MessageProcessingResult.DuplicateIgnored;
            }
        }

        try
        {
            await handler(message.Body);
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
            ResetCircuitBreaker(queueName);

            EasyRabbitTelemetry.ProcessedCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
            return MessageProcessingResult.Processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed for queue '{QueueName}'.", queueName);
            EasyRabbitTelemetry.ErrorCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));

            if (idempotencyTokenAcquired && !string.IsNullOrWhiteSpace(idempotencyMessageId))
            {
                await _idempotencyStore.ReleaseAsync(queueName, idempotencyMessageId!, cancellationToken);
            }

            var processingResult = await HandleProcessingFailureAsync(channel, queueName, queueConfig, topology, message, cancellationToken);
            RegisterFailure(queueName, queueConfig);
            return processingResult;
        }
    }

    private async Task<int> ConsumeManyWorkerAsync(
        string queueName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        ushort prefetchCount,
        ConsumeBudget budget,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        IChannel? workerChannel = null;
        try
        {
            var connection = _connection ?? throw new EasyRabbitConfigurationException("Connection is not initialized.");
            workerChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await workerChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetchCount, global: false, cancellationToken: cancellationToken);

            var processedCount = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!budget.TryReserveSlot())
                {
                    break;
                }

                var result = await ConsumeOnceCoreAsync(workerChannel, queueName, handler, cancellationToken);
                if (result == MessageProcessingResult.NoMessage || result == MessageProcessingResult.CircuitOpen)
                {
                    budget.ReturnSlot();
                    break;
                }

                if (result == MessageProcessingResult.Processed)
                {
                    processedCount++;
                }
            }

            return processedCount;
        }
        finally
        {
            if (workerChannel is not null)
            {
                await workerChannel.DisposeAsync();
            }
        }
    }

    private async Task<MessageProcessingResult> HandleProcessingFailureAsync(
        IChannel channel,
        string queueName,
        QueueSettings queueConfig,
        QueueTopology topology,
        BasicGetResult message,
        CancellationToken cancellationToken)
    {
        var currentAttempt = GetAttemptFromHeaders(message.BasicProperties?.Headers);

        if (currentAttempt >= queueConfig.Retry.MaxAttempts)
        {
            var deadProperties = new BasicProperties
            {
                Persistent = true,
                MessageId = message.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("N"),
                Headers = CloneHeaders(message.BasicProperties?.Headers)
            };

            await channel.BasicPublishAsync(
                exchange: GetDeadExchangeName(queueName),
                routingKey: topology.DeadLetterQueue,
                mandatory: false,
                basicProperties: deadProperties,
                body: message.Body,
                cancellationToken: cancellationToken);

            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken: cancellationToken);

            EasyRabbitTelemetry.DeadLetterCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
            return MessageProcessingResult.MovedToDeadLetter;
        }

        var delayMs = RetryDelayCalculator.CalculateDelayMs(queueConfig.Retry, currentAttempt);

        var retryHeaders = CloneHeaders(message.BasicProperties?.Headers);
        retryHeaders[AttemptHeaderName] = currentAttempt + 1;

        var retryProperties = new BasicProperties
        {
            Persistent = true,
            MessageId = message.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("N"),
            Headers = retryHeaders,
            Expiration = delayMs.ToString(CultureInfo.InvariantCulture)
        };

        await channel.BasicPublishAsync(
            exchange: GetRetryExchangeName(queueName),
            routingKey: topology.RetryQueue,
            mandatory: false,
            basicProperties: retryProperties,
            body: message.Body,
            cancellationToken: cancellationToken);

        await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken: cancellationToken);

        EasyRabbitTelemetry.RetriedCounter.Add(1, new KeyValuePair<string, object?>("queue", queueName));
        return MessageProcessingResult.MovedToRetry;
    }

    private static Dictionary<string, object?> CloneHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null)
        {
            return new Dictionary<string, object?>();
        }

        return headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static int GetAttemptFromHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(AttemptHeaderName, out var rawAttempt))
        {
            return 1;
        }

        return Math.Max(1, ConvertToInt32(rawAttempt));
    }

    private bool IsCircuitOpen(string queueName, QueueSettings queueConfig)
    {
        if (!queueConfig.CircuitBreaker.Enabled)
        {
            return false;
        }

        if (!_circuitBreakerMap.TryGetValue(queueName, out var state))
        {
            return false;
        }

        return state.BreakUntilUtc.HasValue && state.BreakUntilUtc.Value > DateTimeOffset.UtcNow;
    }

    private void RegisterFailure(string queueName, QueueSettings queueConfig)
    {
        if (!_circuitBreakerMap.TryGetValue(queueName, out var state))
        {
            return;
        }

        lock (state.Sync)
        {
            state.ConsecutiveFailures++;

            if (!queueConfig.CircuitBreaker.Enabled)
            {
                return;
            }

            if (state.ConsecutiveFailures >= queueConfig.CircuitBreaker.FailureThreshold)
            {
                state.BreakUntilUtc = DateTimeOffset.UtcNow.AddSeconds(queueConfig.CircuitBreaker.BreakDurationSeconds);
                state.ConsecutiveFailures = 0;
                _logger.LogWarning("Circuit opened for queue '{QueueName}' for {BreakDuration}s.", queueName, queueConfig.CircuitBreaker.BreakDurationSeconds);
            }
        }
    }

    private void ResetCircuitBreaker(string queueName)
    {
        if (!_circuitBreakerMap.TryGetValue(queueName, out var state))
        {
            return;
        }

        lock (state.Sync)
        {
            state.ConsecutiveFailures = 0;
            state.BreakUntilUtc = null;
        }
    }

    private async Task RecreateConnectionCoreAsync(CancellationToken cancellationToken)
    {
        await DisposePublishChannelPoolAsync();

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        var latestConnection = _connectionSettingsProvider();
        var factory = BuildConnectionFactory(latestConnection);
        _connection = await factory.CreateConnectionAsync(cancellationToken);

        await using var topologyChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        foreach (var queue in _settings.Queues)
        {
            var topology = TopologyBuilder.Build(queue.Name);
            await DeclareTopologyAsync(topologyChannel, queue, topology, cancellationToken);
        }
    }

    private bool IsConnectionHealthy()
    {
        return _connection is not null && _connection.IsOpen;
    }

    private Task ExecuteWithRecoveryAsync(Func<IChannel, Task> operation, CancellationToken cancellationToken)
    {
        return ExecuteWithRecoveryAsync(
            async channel =>
            {
                await operation(channel);
                return true;
            },
            cancellationToken);
    }

    private Task<T> ExecuteWithRecoveryAsync<T>(Func<IChannel, Task<T>> operation, CancellationToken cancellationToken)
    {
        return ExecuteWithRecoveryOnConnectionAsync(
            async connection =>
            {
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                return await operation(channel);
            },
            cancellationToken);
    }

    private async Task<T> ExecuteWithRecoveryOnConnectionAsync<T>(Func<IConnection, Task<T>> operation, CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        Exception? lastRecoverableException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await InitializeAsync(cancellationToken);
                var connection = _connection ?? throw new EasyRabbitConfigurationException("RabbitMQ connection is not available.");
                return await operation(connection);
            }
            catch (Exception ex) when (IsRecoverable(ex) && _settings.Connection.AutomaticRecoveryEnabled && attempt < maxAttempts)
            {
                lastRecoverableException = ex;
                EasyRabbitTelemetry.RecoveryCounter.Add(1);
                _logger.LogWarning(ex, "Recoverable RabbitMQ error detected. Recovery attempt {Attempt}/{MaxAttempts}.", attempt, maxAttempts);

                try
                {
                    await RecoverConnectionAsync(cancellationToken);
                }
                catch (Exception recoveryException) when (IsRecoverable(recoveryException))
                {
                    throw new EasyRabbitConfigurationException(
                        "Automatic RabbitMQ recovery failed while handling a recoverable operation error.",
                        new AggregateException(ex, recoveryException));
                }

                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex) when (IsRecoverable(ex) && _settings.Connection.AutomaticRecoveryEnabled && attempt == maxAttempts)
            {
                lastRecoverableException = ex;
            }
        }

        throw lastRecoverableException is null
            ? new EasyRabbitConfigurationException("Operation failed after recovery attempts.")
            : new EasyRabbitConfigurationException("Operation failed after recovery attempts.", lastRecoverableException);
    }

    private async Task RecoverConnectionAsync(CancellationToken cancellationToken)
    {
        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            _initialized = false;
            await RecreateConnectionCoreAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task<IChannel> RentPublishChannelAsync(IConnection connection, CancellationToken cancellationToken)
    {
        while (_publishChannelPool.TryTake(out var pooledChannel))
        {
            Interlocked.Decrement(ref _publishIdleChannels);

            if (pooledChannel.IsOpen)
            {
                return pooledChannel;
            }

            await pooledChannel.DisposeAsync();
        }

        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    private async Task ReturnPublishChannelAsync(IChannel channel)
    {
        if (!channel.IsOpen)
        {
            await channel.DisposeAsync();
            return;
        }

        var idleAfterIncrement = Interlocked.Increment(ref _publishIdleChannels);
        if (idleAfterIncrement <= _maxPublishChannelPoolSize)
        {
            _publishChannelPool.Add(channel);
            return;
        }

        Interlocked.Decrement(ref _publishIdleChannels);
        await channel.DisposeAsync();
    }

    private async Task DisposePublishChannelPoolAsync()
    {
        while (_publishChannelPool.TryTake(out var channel))
        {
            Interlocked.Decrement(ref _publishIdleChannels);
            await channel.DisposeAsync();
        }
    }

    private static bool IsRecoverable(Exception exception)
    {
        if (exception is AlreadyClosedException or BrokerUnreachableException or OperationInterruptedException or IOException)
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(IsRecoverable);
        }

        if (exception.InnerException is SocketException)
        {
            return true;
        }

        return exception.InnerException is not null && IsRecoverable(exception.InnerException);
    }

    private static async Task DeclareTopologyAsync(
        IChannel channel,
        QueueSettings queue,
        QueueTopology topology,
        CancellationToken cancellationToken)
    {
        var mainExchange = GetMainExchangeName(queue.Name);
        var retryExchange = GetRetryExchangeName(queue.Name);
        var deadExchange = GetDeadExchangeName(queue.Name);

        await channel.ExchangeDeclareAsync(mainExchange, ExchangeType.Direct, queue.Durable, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(retryExchange, ExchangeType.Direct, queue.Durable, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(deadExchange, ExchangeType.Direct, queue.Durable, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: topology.MainQueue,
            durable: queue.Durable,
            exclusive: false,
            autoDelete: queue.AutoDelete,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: topology.RetryQueue,
            durable: queue.Durable,
            exclusive: false,
            autoDelete: queue.AutoDelete,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = mainExchange,
                ["x-dead-letter-routing-key"] = topology.MainQueue
            },
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: topology.DeadLetterQueue,
            durable: queue.Durable,
            exclusive: false,
            autoDelete: queue.AutoDelete,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(topology.MainQueue, mainExchange, topology.MainQueue, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(topology.RetryQueue, retryExchange, topology.RetryQueue, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(topology.DeadLetterQueue, deadExchange, topology.DeadLetterQueue, cancellationToken: cancellationToken);
    }

    private static ConnectionFactory BuildConnectionFactory(ConnectionSettings connection)
    {
        return new ConnectionFactory
        {
            HostName = connection.HostName,
            Port = connection.Port,
            VirtualHost = connection.VirtualHost,
            UserName = connection.UserName,
            Password = connection.Password,
            ClientProvidedName = connection.ClientProvidedName,
            AutomaticRecoveryEnabled = connection.AutomaticRecoveryEnabled,
            TopologyRecoveryEnabled = connection.TopologyRecoveryEnabled,
            RequestedHeartbeat = TimeSpan.FromSeconds(connection.RequestedHeartbeatSeconds)
        };
    }

    private QueueSettings GetQueueSettings(string queueName)
    {
        if (_queueSettingsMap.TryGetValue(queueName, out var queueSettings))
        {
            return queueSettings;
        }

        throw new EasyRabbitConfigurationException($"Queue '{queueName}' was not configured.");
    }

    private static string? ResolveMessageId(QueueSettings queueConfig, IReadOnlyBasicProperties? properties)
    {
        if (properties is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(properties.MessageId))
        {
            return properties.MessageId;
        }

        if (properties.Headers is null || !properties.Headers.TryGetValue(queueConfig.Idempotency.HeaderName, out var headerValue))
        {
            return null;
        }

        return headerValue switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> rom => Encoding.UTF8.GetString(rom.Span),
            _ => headerValue?.ToString()
        };
    }
    private static int ConvertToInt32(object? value)
    {
        return value switch
        {
            null => 0,
            int i => i,
            long l => (int)l,
            byte b => b,
            short s => s,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsedBytes) => parsedBytes,
            ReadOnlyMemory<byte> memory when int.TryParse(Encoding.UTF8.GetString(memory.Span), out var parsedMemory) => parsedMemory,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static string GetMainExchangeName(string queueName) => $"{queueName}.exchange";

    private static string GetRetryExchangeName(string queueName) => $"{queueName}.retry.exchange";

    private static string GetDeadExchangeName(string queueName) => $"{queueName}.dead.exchange";

    private sealed class ConsumeBudget
    {
        private int _remaining;

        public ConsumeBudget(int maxMessages)
        {
            _remaining = maxMessages;
        }

        public bool TryReserveSlot()
        {
            var remainingAfterReservation = Interlocked.Decrement(ref _remaining);
            if (remainingAfterReservation >= 0)
            {
                return true;
            }

            Interlocked.Increment(ref _remaining);
            return false;
        }

        public void ReturnSlot()
        {
            Interlocked.Increment(ref _remaining);
        }
    }

    private sealed class CircuitBreakerState
    {
        public object Sync { get; } = new();

        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset? BreakUntilUtc { get; set; }
    }
}


















