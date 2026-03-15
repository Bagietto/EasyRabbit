using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyRabbitMQ.Configuration;
using EasyRabbitMQ.IntegrationTests.Infrastructure;
using EasyRabbitMQ.Resilience;
using EasyRabbitMQ.Runtime;
using EasyRabbitMQ.Topology;
using FluentAssertions;
using Polly;
using RabbitMQ.Client;

namespace EasyRabbitMQ.IntegrationTests.Resilience;

[Collection(RabbitMqIntegrationCollection.Name)]
public sealed class RabbitMqRetryAndDeadLetterIntegrationTests
{
    private readonly RabbitMqIntegrationFixture _fixture;

    public RabbitMqRetryAndDeadLetterIntegrationTests(RabbitMqIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Should_Process_Message_Successfully_Without_Moving_To_Retry_Or_Dead()
    {
        // Given
        var queueName = $"orders-success.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 2,
            FixedDelayMs = 1000
        };

        await using var runtime = new EasyRabbitRuntime(BuildSettings(queueName, retrySettings));
        var topology = TopologyBuilder.Build(queueName);

        await runtime.InitializeAsync();
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":111,\"status\":\"ready\"}"));

        await WaitUntilAsync(async () => await runtime.GetQueueMessageCountAsync(topology.MainQueue) > 0, TimeSpan.FromSeconds(10));

        // When
        var outcome = await runtime.ConsumeOnceAsync(queueName, _ => Task.CompletedTask);

        // Then
        outcome.Should().Be(MessageProcessingResult.Processed);
        (await runtime.GetQueueMessageCountAsync(topology.MainQueue)).Should().Be(0);
        (await runtime.GetQueueMessageCountAsync(topology.RetryQueue)).Should().Be(0);
        (await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue)).Should().Be(0);
    }

    [Fact]
    public async Task Should_Return_NoMessage_When_MainQueue_Is_Empty()
    {
        // Given
        var queueName = $"orders-empty.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 2,
            FixedDelayMs = 1000
        };

        await using var runtime = new EasyRabbitRuntime(BuildSettings(queueName, retrySettings));
        var topology = TopologyBuilder.Build(queueName);

        await runtime.InitializeAsync();

        // When
        var outcome = await runtime.ConsumeOnceAsync(queueName, _ => Task.CompletedTask);

        // Then
        outcome.Should().Be(MessageProcessingResult.NoMessage);
        (await runtime.GetQueueMessageCountAsync(topology.MainQueue)).Should().Be(0);
        (await runtime.GetQueueMessageCountAsync(topology.RetryQueue)).Should().Be(0);
        (await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue)).Should().Be(0);
    }

    [Fact]
    public async Task Should_Move_Message_To_Retry_Return_To_Main_And_Then_To_Dead_Using_TaskDelay_Strategy()
    {
        // Given
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 2,
            FixedDelayMs = 1200
        };

        // When
        var result = await RunResilienceScenarioAsync(
            queuePrefix: "orders-delay",
            retrySettings,
            waitForMainAfterRetryAsync: async (runtime, topology, retryTtlMs) =>
            {
                await Task.Delay(retryTtlMs + 300);
                await WaitUntilAsync(async () => await runtime.GetQueueMessageCountAsync(topology.MainQueue) > 0, TimeSpan.FromSeconds(10));
            });

        // Then
        result.Should().Be(MessageProcessingResult.MovedToDeadLetter);
    }

    [Fact]
    public async Task Should_Move_Message_To_Retry_Return_To_Main_And_Then_To_Dead_Using_Polly_Strategy()
    {
        // Given
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 2,
            FixedDelayMs = 1200
        };

        // When
        var result = await RunResilienceScenarioAsync(
            queuePrefix: "orders-polly",
            retrySettings,
            waitForMainAfterRetryAsync: async (runtime, topology, _) =>
            {
                await WaitUntilWithPollyAsync(
                    async () => await runtime.GetQueueMessageCountAsync(topology.MainQueue) > 0,
                    timeout: TimeSpan.FromSeconds(10),
                    retryInterval: TimeSpan.FromMilliseconds(200));
            });

        // Then
        result.Should().Be(MessageProcessingResult.MovedToDeadLetter);
    }

    [Fact]
    public async Task Should_Apply_Exponential_Retry_Delay_Across_Multiple_Attempts()
    {
        // Given
        var queueName = $"orders-exponential.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Exponential,
            MaxAttempts = 3,
            InitialDelayMs = 300,
            Multiplier = 2.0,
            MaxDelayMs = 2000
        };

        await using var runtime = new EasyRabbitRuntime(BuildSettings(queueName, retrySettings));
        var topology = TopologyBuilder.Build(queueName);

        await runtime.InitializeAsync();
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":333,\"status\":\"exponential\"}"));

        // When
        var firstOutcome = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("Fail 1"));
        firstOutcome.Should().Be(MessageProcessingResult.MovedToRetry);

        var firstDelay = await MeasureTimeUntilMainQueueHasMessageAsync(runtime, topology.MainQueue, TimeSpan.FromSeconds(10));

        var secondOutcome = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("Fail 2"));
        secondOutcome.Should().Be(MessageProcessingResult.MovedToRetry);

        var secondDelay = await MeasureTimeUntilMainQueueHasMessageAsync(runtime, topology.MainQueue, TimeSpan.FromSeconds(10));

        var thirdOutcome = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("Fail 3"));

        // Then
        thirdOutcome.Should().Be(MessageProcessingResult.MovedToDeadLetter);
        secondDelay.Should().BeGreaterThan(firstDelay);
        secondDelay.Should().BeGreaterThanOrEqualTo(firstDelay + TimeSpan.FromMilliseconds(120));
        (await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue)).Should().Be(1);
    }

    [Fact]
    public async Task Should_Respect_PrefetchCount_And_Workers_Under_Load()
    {
        // Given
        var queueName = $"orders-workers.{Guid.NewGuid():N}";
        const int totalMessages = 20;

        var settings = BuildSettings(
            queueName,
            new RetrySettings
            {
                Mode = RetryMode.Fixed,
                MaxAttempts = 3,
                FixedDelayMs = 1000
            },
            workers: 4,
            prefetchCount: 1);

        await using var runtime = new EasyRabbitRuntime(settings);

        await runtime.InitializeAsync();

        for (var i = 0; i < totalMessages; i++)
        {
            await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes($"{{\"orderId\":{i}}}"));
        }

        var inFlight = 0;
        var maxConcurrency = 0;

        // When
        var processed = await runtime.ConsumeManyAsync(
            queueName,
            maxMessages: totalMessages,
            async _ =>
            {
                var current = Interlocked.Increment(ref inFlight);
                UpdateMax(ref maxConcurrency, current);

                await Task.Delay(120);

                Interlocked.Decrement(ref inFlight);
            });

        // Then
        processed.Should().Be(totalMessages);
        maxConcurrency.Should().BeGreaterThan(1);
        maxConcurrency.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task Should_Open_And_Close_CircuitBreaker_After_Configured_Failures()
    {
        // Given
        var queueName = $"orders-circuit.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 5,
            FixedDelayMs = 5000
        };

        await using var runtime = new EasyRabbitRuntime(
            BuildSettings(
                queueName,
                retrySettings,
                workers: 1,
                prefetchCount: 10,
                circuitEnabled: true,
                circuitFailureThreshold: 2,
                circuitBreakSeconds: 2));

        var topology = TopologyBuilder.Build(queueName);

        await runtime.InitializeAsync();
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":1}"));
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":2}"));
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":3}"));

        // When
        var first = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("failure 1"));
        var second = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("failure 2"));
        var third = await runtime.ConsumeOnceAsync(queueName, _ => Task.CompletedTask);

        await Task.Delay(TimeSpan.FromSeconds(2.2));

        var afterBreak = await runtime.ConsumeOnceAsync(queueName, _ => Task.CompletedTask);

        // Then
        first.Should().Be(MessageProcessingResult.MovedToRetry);
        second.Should().Be(MessageProcessingResult.MovedToRetry);
        third.Should().Be(MessageProcessingResult.CircuitOpen);
        afterBreak.Should().Be(MessageProcessingResult.Processed);
        (await runtime.GetQueueMessageCountAsync(topology.MainQueue)).Should().Be(0);
    }

    [Fact]
    public async Task Should_Recover_After_Broker_Restart_And_Continue_Processing()
    {
        // Given
        var queueName = $"orders-recovery.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 3,
            FixedDelayMs = 1000
        };

        var runtimeSettings = BuildSettings(queueName, retrySettings);

        await using var runtime = new EasyRabbitRuntime(runtimeSettings, () => new ConnectionSettings
        {
            HostName = _fixture.HostName,
            Port = _fixture.AmqpPort,
            VirtualHost = runtimeSettings.Connection.VirtualHost,
            UserName = runtimeSettings.Connection.UserName,
            Password = runtimeSettings.Connection.Password,
            ClientProvidedName = runtimeSettings.Connection.ClientProvidedName,
            RequestedHeartbeatSeconds = runtimeSettings.Connection.RequestedHeartbeatSeconds,
            AutomaticRecoveryEnabled = runtimeSettings.Connection.AutomaticRecoveryEnabled,
            TopologyRecoveryEnabled = runtimeSettings.Connection.TopologyRecoveryEnabled
        });
        await runtime.InitializeAsync();
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":901}"));

        // When
        await _fixture.RestartAsync();
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":902}"));

        var processed = 0;
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < timeout && processed < 2)
        {
            var result = await runtime.ConsumeOnceAsync(queueName, _ => Task.CompletedTask);
            if (result == MessageProcessingResult.Processed)
            {
                processed++;
            }
            else
            {
                await Task.Delay(200);
            }
        }

        // Then
        processed.Should().Be(2);
    }

    [Fact]
    public async Task Should_Ignore_Duplicate_Message_When_Idempotency_Is_Enabled()
    {
        // Given
        var queueName = $"orders-idempotency.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 2,
            FixedDelayMs = 1000
        };

        await using var runtime = new EasyRabbitRuntime(
            BuildSettings(
                queueName,
                retrySettings,
                workers: 1,
                prefetchCount: 10,
                circuitEnabled: false,
                circuitFailureThreshold: 2,
                circuitBreakSeconds: 2,
                idempotencyEnabled: true));

        var processedCount = 0;
        const string messageId = "msg-duplicate-001";

        await runtime.InitializeAsync();

        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":7001}"), messageId: messageId);
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":7001}"), messageId: messageId);

        // When
        var firstOutcome = await runtime.ConsumeOnceAsync(queueName, _ =>
        {
            Interlocked.Increment(ref processedCount);
            return Task.CompletedTask;
        });

        var secondOutcome = await runtime.ConsumeOnceAsync(queueName, _ =>
        {
            Interlocked.Increment(ref processedCount);
            return Task.CompletedTask;
        });

        // Then
        firstOutcome.Should().Be(MessageProcessingResult.Processed);
        secondOutcome.Should().Be(MessageProcessingResult.DuplicateIgnored);
        processedCount.Should().Be(1);
    }

    [Fact]
    public async Task Should_Replay_Message_From_DeadLetter_To_MainQueue()
    {
        // Given
        var queueName = $"orders-replay.{Guid.NewGuid():N}";
        var retrySettings = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 1,
            FixedDelayMs = 1000
        };

        await using var runtime = new EasyRabbitRuntime(BuildSettings(queueName, retrySettings));
        var topology = TopologyBuilder.Build(queueName);

        await runtime.InitializeAsync();
        await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes("{\"orderId\":8011}"), messageId: "msg-replay-1");

        var firstOutcome = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("force dead"));
        firstOutcome.Should().Be(MessageProcessingResult.MovedToDeadLetter);

        await WaitUntilAsync(async () => await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue) == 1, TimeSpan.FromSeconds(10));

        // When
        var replayed = await runtime.ReplayDeadLetterAsync(queueName, maxMessages: 10);

        var processed = await runtime.ConsumeOnceAsync(queueName, _ => Task.CompletedTask);

        // Then
        replayed.Should().Be(1);
        processed.Should().Be(MessageProcessingResult.Processed);
        (await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue)).Should().Be(0);
    }
    [Fact]
    public async Task ConsumeManyAsync_Should_Not_Exceed_Global_MaxMessages_With_Multiple_Workers()
    {
        // Given
        var queueName = $"orders-maxmessages.{Guid.NewGuid():N}";
        const int totalMessages = 20;
        const int maxMessages = 5;

        var settings = BuildSettings(
            queueName,
            new RetrySettings
            {
                Mode = RetryMode.Fixed,
                MaxAttempts = 3,
                FixedDelayMs = 1000
            },
            workers: 4,
            prefetchCount: 1);

        await using var runtime = new EasyRabbitRuntime(settings);
        var topology = TopologyBuilder.Build(queueName);

        await runtime.InitializeAsync();

        for (var i = 0; i < totalMessages; i++)
        {
            await runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes($"{{\"orderId\":{i}}}"));
        }

        // When
        var processed = await runtime.ConsumeManyAsync(
            queueName,
            maxMessages,
            async _ => await Task.Delay(40));

        // Then
        processed.Should().Be(maxMessages);
        (await runtime.GetQueueMessageCountAsync(topology.MainQueue)).Should().Be((uint)(totalMessages - maxMessages));
    }
    [Fact]
    public async Task Should_Handle_Constant_Traffic_With_Dedicated_Consumer_Channel_And_Publish_Pool()
    {
        // Given
        var queueName = $"orders-constant-traffic.{Guid.NewGuid():N}";
        const int totalMessages = 300;

        var settings = BuildSettings(
            queueName,
            new RetrySettings
            {
                Mode = RetryMode.Fixed,
                MaxAttempts = 3,
                FixedDelayMs = 1000
            },
            workers: 4,
            prefetchCount: 20);

        settings.Connection.PublishChannelPoolSize = 16;

        await using var runtime = new EasyRabbitRuntime(settings);
        await runtime.InitializeAsync();

        var processed = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var consumerLoop = runtime.RunConsumerLoopAsync(
            queueName,
            _ =>
            {
                if (Interlocked.Increment(ref processed) >= totalMessages)
                {
                    cts.Cancel();
                }

                return Task.CompletedTask;
            },
            idleDelay: TimeSpan.FromMilliseconds(5),
            cancellationToken: cts.Token);

        // When
        var publishTasks = Enumerable.Range(0, totalMessages)
            .Select(i => runtime.PublishAsync(queueName, Encoding.UTF8.GetBytes($"{{\"orderId\":{i}}}"), cancellationToken: cts.Token));

        await Task.WhenAll(publishTasks);
        await WaitUntilAsync(() => Task.FromResult(Volatile.Read(ref processed) >= totalMessages), TimeSpan.FromSeconds(20));

        cts.Cancel();

        try
        {
            await consumerLoop;
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }

        // Then
        processed.Should().Be(totalMessages);
    }

    private async Task<MessageProcessingResult> RunResilienceScenarioAsync(
        string queuePrefix,
        RetrySettings retrySettings,
        Func<IEasyRabbitRuntime, QueueTopology, int, Task> waitForMainAfterRetryAsync)
    {
        var queueName = $"{queuePrefix}.{Guid.NewGuid():N}";
        var topology = TopologyBuilder.Build(queueName);

        var mainExchange = $"{queueName}.exchange";
        var retryExchange = $"{queueName}.retry.exchange";
        var deadExchange = $"{queueName}.dead.exchange";

        var retryTtlMs = RetryDelayCalculator.CalculateDelayMs(retrySettings, attempt: 1);

        var settings = BuildSettings(queueName, retrySettings);

        await using var runtime = new EasyRabbitRuntime(settings);
        using var managementClient = CreateManagementHttpClient();

        await runtime.InitializeAsync();

        await AssertTopologyExistsViaManagementApiAsync(managementClient, topology, mainExchange, retryExchange, deadExchange);

        var body = Encoding.UTF8.GetBytes("{\"orderId\":123,\"status\":\"pending\"}");
        await runtime.PublishAsync(queueName, body);

        var firstOutcome = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("Simulated consumer exception."));
        firstOutcome.Should().Be(MessageProcessingResult.MovedToRetry);

        await WaitUntilAsync(async () => await runtime.GetQueueMessageCountAsync(topology.RetryQueue) > 0, TimeSpan.FromSeconds(10));
        await waitForMainAfterRetryAsync(runtime, topology, retryTtlMs);

        var secondOutcome = await runtime.ConsumeOnceAsync(queueName, _ => throw new InvalidOperationException("Simulated consumer exception."));
        secondOutcome.Should().Be(MessageProcessingResult.MovedToDeadLetter);

        await WaitUntilAsync(async () => await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue) == 1, TimeSpan.FromSeconds(10));
        (await runtime.GetQueueMessageCountAsync(topology.RetryQueue)).Should().Be(0);
        (await runtime.GetQueueMessageCountAsync(topology.MainQueue)).Should().Be(0);
        (await runtime.GetQueueMessageCountAsync(topology.DeadLetterQueue)).Should().Be(1);

        return secondOutcome;
    }

    private EasyRabbitMQSettings BuildSettings(
        string queueName,
        RetrySettings retrySettings,
        int workers = 1,
        ushort prefetchCount = 10,
        bool circuitEnabled = false,
        int circuitFailureThreshold = 2,
        int circuitBreakSeconds = 2,
        bool idempotencyEnabled = false,
        string idempotencyHeaderName = "message-id",
        int idempotencyCacheTtlMinutes = 60,
        int idempotencyMaxTracked = 10000)
    {
        return new EasyRabbitMQSettings
        {
            Connection = new ConnectionSettings
            {
                HostName = _fixture.HostName,
                Port = _fixture.AmqpPort,
                VirtualHost = "/",
                UserName = _fixture.UserName,
                Password = _fixture.Password,
                ClientProvidedName = "EasyRabbitMQ.IntegrationTests",
                RequestedHeartbeatSeconds = 30,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            },
            Queues = new[]
            {
                new QueueSettings
                {
                    Name = queueName,
                    Durable = true,
                    AutoDelete = false,
                    PrefetchCount = prefetchCount,
                    Workers = workers,
                    Retry = retrySettings,
                    CircuitBreaker = new CircuitBreakerSettings
                    {
                        Enabled = circuitEnabled,
                        FailureThreshold = circuitFailureThreshold,
                        BreakDurationSeconds = circuitBreakSeconds
                    },
                    Idempotency = new IdempotencySettings
                    {
                        Enabled = idempotencyEnabled,
                        HeaderName = idempotencyHeaderName,
                        CacheTtlMinutes = idempotencyCacheTtlMinutes,
                        MaxTrackedMessageIds = idempotencyMaxTracked
                    }
                }
            }
        };
    }

    private HttpClient CreateManagementHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = _fixture.ManagementBaseUri
        };

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_fixture.UserName}:{_fixture.Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

        return client;
    }

    private static async Task AssertTopologyExistsViaManagementApiAsync(
        HttpClient managementClient,
        QueueTopology topology,
        string mainExchange,
        string retryExchange,
        string deadExchange)
    {
        var mainExchangeJson = await GetJsonAsync(managementClient, $"exchanges/%2F/{Uri.EscapeDataString(mainExchange)}");
        var retryExchangeJson = await GetJsonAsync(managementClient, $"exchanges/%2F/{Uri.EscapeDataString(retryExchange)}");
        var deadExchangeJson = await GetJsonAsync(managementClient, $"exchanges/%2F/{Uri.EscapeDataString(deadExchange)}");

        AssertExchange(mainExchangeJson.RootElement, mainExchange);
        AssertExchange(retryExchangeJson.RootElement, retryExchange);
        AssertExchange(deadExchangeJson.RootElement, deadExchange);

        var mainQueueJson = await GetJsonAsync(managementClient, $"queues/%2F/{Uri.EscapeDataString(topology.MainQueue)}");
        var retryQueueJson = await GetJsonAsync(managementClient, $"queues/%2F/{Uri.EscapeDataString(topology.RetryQueue)}");
        var deadQueueJson = await GetJsonAsync(managementClient, $"queues/%2F/{Uri.EscapeDataString(topology.DeadLetterQueue)}");

        AssertQueue(mainQueueJson.RootElement, topology.MainQueue, expectedDurable: true);
        AssertQueue(retryQueueJson.RootElement, topology.RetryQueue, expectedDurable: true);
        AssertQueue(deadQueueJson.RootElement, topology.DeadLetterQueue, expectedDurable: true);

        var retryArgs = retryQueueJson.RootElement.GetProperty("arguments");
        retryArgs.GetProperty("x-dead-letter-exchange").GetString().Should().Be(mainExchange);
        retryArgs.GetProperty("x-dead-letter-routing-key").GetString().Should().Be(topology.MainQueue);

        var mainBindings = await GetJsonAsync(managementClient, $"bindings/%2F/e/{Uri.EscapeDataString(mainExchange)}/q/{Uri.EscapeDataString(topology.MainQueue)}");
        var retryBindings = await GetJsonAsync(managementClient, $"bindings/%2F/e/{Uri.EscapeDataString(retryExchange)}/q/{Uri.EscapeDataString(topology.RetryQueue)}");
        var deadBindings = await GetJsonAsync(managementClient, $"bindings/%2F/e/{Uri.EscapeDataString(deadExchange)}/q/{Uri.EscapeDataString(topology.DeadLetterQueue)}");

        HasBinding(mainBindings.RootElement, topology.MainQueue).Should().BeTrue();
        HasBinding(retryBindings.RootElement, topology.RetryQueue).Should().BeTrue();
        HasBinding(deadBindings.RootElement, topology.DeadLetterQueue).Should().BeTrue();
    }

    private static async Task<TimeSpan> MeasureTimeUntilMainQueueHasMessageAsync(
        IEasyRabbitRuntime runtime,
        string mainQueue,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        await WaitUntilAsync(async () => await runtime.GetQueueMessageCountAsync(mainQueue) > 0, timeout);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        while (true)
        {
            var current = target;
            if (candidate <= current)
            {
                return;
            }

            var updated = Interlocked.CompareExchange(ref target, candidate, current);
            if (updated == current)
            {
                return;
            }
        }
    }

    private static bool HasBinding(JsonElement bindings, string routingKey)
    {
        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.GetProperty("routing_key").GetString() == routingKey)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertExchange(JsonElement exchange, string expectedName)
    {
        exchange.GetProperty("name").GetString().Should().Be(expectedName);
        exchange.GetProperty("type").GetString().Should().Be(ExchangeType.Direct);
        exchange.GetProperty("durable").GetBoolean().Should().BeTrue();
    }

    private static void AssertQueue(JsonElement queue, string expectedName, bool expectedDurable)
    {
        queue.GetProperty("name").GetString().Should().Be(expectedName);
        queue.GetProperty("durable").GetBoolean().Should().Be(expectedDurable);
        queue.GetProperty("auto_delete").GetBoolean().Should().BeFalse();
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string relativePath)
    {
        using var response = await client.GetAsync(relativePath);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Condition was not met within {timeout.TotalSeconds:F1} seconds.");
    }

    private static async Task WaitUntilWithPollyAsync(Func<Task<bool>> condition, TimeSpan timeout, TimeSpan retryInterval)
    {
        var retries = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds / retryInterval.TotalMilliseconds));

        var policy = Policy
            .HandleResult<bool>(result => result is false)
            .WaitAndRetryAsync(retries, _ => retryInterval);

        var completed = await policy.ExecuteAsync(async () => await condition());
        completed.Should().BeTrue($"Condition was not met within {timeout.TotalSeconds:F1} seconds.");
    }
}












