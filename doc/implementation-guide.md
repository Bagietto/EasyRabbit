# Implementation Guide

This guide explains how to implement each runtime mode and when each one is useful.

## Hosted Service (recommended)

How to implement:

```csharp
builder.Services.AddEasyRabbitMQ(builder.Configuration);
builder.Services.AddEasyRabbitMQHostedConsumers();
builder.Services.AddSingleton<IEasyRabbitMessageHandler, OrdersHandler>();
```

Why it is useful:

- Continuous consumption with host lifecycle integration
- Graceful shutdown using `stoppingToken`
- Best default for APIs and worker services in production

## Manual Runtime

How to implement:

```csharp
await runtime.InitializeAsync(ct);
await runtime.PublishAsync("orders", body, cancellationToken: ct);
await runtime.ConsumeOnceAsync("orders", ProcessAsync, ct);
```

Why it is useful:

- On-demand jobs
- Operational scripts and maintenance routines
- Fine-grained consumption control

## Batch Consumption (`ConsumeManyAsync`)

How to implement:

```csharp
var processed = await runtime.ConsumeManyAsync(
    queueName: "orders",
    maxMessages: 500,
    handler: ProcessAsync,
    cancellationToken: ct);
```

Why it is useful:

- Controlled queue draining
- Scheduled processing windows
- Bounded non-infinite processing loops

## Dedicated Consumer Loop (`RunConsumerLoopAsync`)

How to implement:

```csharp
await runtime.RunConsumerLoopAsync(
    queueName: "orders",
    handler: ProcessAsync,
    idleDelay: TimeSpan.FromMilliseconds(100),
    cancellationToken: ct);
```

Why it is useful:

- Continuous polling with a dedicated channel
- Reduced overhead under steady traffic
- Explicit control of polling cadence

## Dead-Letter Replay

How to implement:

```csharp
var replayed = await runtime.ReplayDeadLetterAsync(
    queueName: "orders",
    maxMessages: 1000,
    cancellationToken: ct);
```

Why it is useful:

- Reprocessing after bug fix or incident resolution
- Bounded recovery operations
- Avoiding message loss for previously failed items

## Distributed Idempotency (Redis)

How to implement:

```csharp
builder.Services.AddEasyRabbitMQRedisIdempotencyStore(options =>
{
    options.ConnectionString = "localhost:6379";
    options.KeyPrefix = "myapp:easyrabbit:idempotency";
});
```

Why it is useful:

- Deduplication across multiple service instances
- Safer horizontal scaling
- Lower risk of duplicate processing in clustered deployments

## Health Checks

How to implement:

```csharp
builder.Services.AddHealthChecks().AddEasyRabbitMQHealthCheck();
app.MapHealthChecks("/health");
```

Why it is useful:

- Readiness and liveness integration (Kubernetes/App Service)
- Early detection of connectivity/runtime issues
- Standard operational observability

## CancellationToken best practices

- Pass `CancellationToken` through all async calls
- Treat `OperationCanceledException` as expected shutdown behavior
- Reuse host cancellation token in background workloads

Example:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await runtime.PublishAsync("orders", Encoding.UTF8.GetBytes("{}"), cancellationToken: cts.Token);
```
