# Runtime API Guide

This document explains each runtime call and when to use it.

## Initialize

```csharp
await runtime.InitializeAsync(ct);
```

Use it to:

- Open connection
- Declare configured topology
- Fail fast if connection/topology is invalid

## Publish

```csharp
await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes(json),
    messageId: "order-2026-0001",
    headers: new Dictionary<string, object?>
    {
        ["tenant"] = "acme",
        ["source"] = "checkout-api"
    },
    cancellationToken: ct);
```

Use it to:

- Publish to main queue exchange
- Attach explicit `messageId` for idempotency
- Add metadata headers for traceability

## Consume one message

```csharp
var result = await runtime.ConsumeOnceAsync(
    queueName: "orders",
    handler: ProcessAsync,
    cancellationToken: ct);
```

Use it to:

- Polling-style jobs
- Diagnostics
- Controlled single-message handling

Possible results include:

- `Processed`
- `NoMessage`
- `MovedToRetry`
- `MovedToDeadLetter`
- `CircuitOpen`
- `DuplicateIgnored`

## Consume many messages

```csharp
var processed = await runtime.ConsumeManyAsync(
    queueName: "orders",
    maxMessages: 100,
    handler: ProcessAsync,
    cancellationToken: ct);
```

Use it to:

- Batch processing with bounded volume
- Fast queue draining jobs
- Periodic processing tasks

## Dedicated consumer loop

```csharp
await runtime.RunConsumerLoopAsync(
    queueName: "orders",
    handler: ProcessAsync,
    idleDelay: TimeSpan.FromMilliseconds(100),
    cancellationToken: ct);
```

Use it to:

- Continuous polling workers
- Dedicated channel consumption loops
- Lower channel churn under constant traffic

## Replay dead-letter messages

```csharp
var replayed = await runtime.ReplayDeadLetterAsync(
    queueName: "orders",
    maxMessages: 250,
    cancellationToken: ct);
```

Use it to:

- Reprocess previously failed messages
- Controlled recovery with explicit upper bounds

## Inspect queue size

```csharp
var count = await runtime.GetQueueMessageCountAsync("orders", ct);
```

Use it to:

- Operational checks
- Backlog tracking and runtime decisions
