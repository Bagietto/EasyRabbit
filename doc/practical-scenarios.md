# Practical Scenarios and Business Value

This document explains, in practical terms, how EasyRabbitMQ helps in day-to-day operations and includes a full production scenario.

## 1. Complete operation scenario (E-commerce)

### Context

You have an API that receives orders and a worker that processes payment and fulfillment.

- API publishes `OrderCreated`
- Worker consumes from `orders`
- External dependencies (payment/fraud/shipping) may fail temporarily

### Target behavior

- No message is lost
- Temporary failures are retried with delay
- Permanent failures are isolated in dead-letter
- Duplicate messages do not cause double processing
- Operations can replay failed messages after a fix

### Recommended queue configuration

```json
{
  "EasyRabbitMQ": {
    "Connection": {
      "HostName": "rabbitmq",
      "Port": 5672,
      "UserName": "app_user",
      "Password": "app_password",
      "AutomaticRecoveryEnabled": true,
      "TopologyRecoveryEnabled": true,
      "PublishChannelPoolSize": 32
    },
    "Queues": [
      {
        "Name": "orders",
        "Durable": true,
        "PrefetchCount": 30,
        "Workers": 6,
        "Retry": {
          "Mode": "Exponential",
          "MaxAttempts": 6,
          "InitialDelayMs": 1000,
          "MaxDelayMs": 120000,
          "Multiplier": 2.0
        },
        "CircuitBreaker": {
          "Enabled": true,
          "FailureThreshold": 5,
          "BreakDurationSeconds": 20
        },
        "Idempotency": {
          "Enabled": true,
          "HeaderName": "message-id",
          "CacheTtlMinutes": 120,
          "MaxTrackedMessageIds": 50000
        }
      }
    ]
  }
}
```

### API publish example

```csharp
await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes(orderJson),
    messageId: $"order-{orderId}",
    headers: new Dictionary<string, object?>
    {
        ["tenant"] = tenantId,
        ["source"] = "orders-api"
    },
    cancellationToken: ct);
```

### Worker handler example

```csharp
public sealed class OrdersHandler : IEasyRabbitMessageHandler
{
    public string QueueName => "orders";

    public async Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(body.Span);

        // parse order and call external dependencies
        // throw on failure -> runtime routes to retry or dead-letter
        await Task.CompletedTask;
    }
}
```

### Incident recovery example

After fixing a bug, replay dead-letter messages:

```csharp
var replayed = await runtime.ReplayDeadLetterAsync(
    queueName: "orders",
    maxMessages: 5000,
    cancellationToken: ct);
```

## 2. How each feature helps in practice

### Fail-fast configuration validation

What it does:

- Rejects invalid config at startup

Why it helps:

- Prevents silent misconfiguration in production
- Fails early with clear diagnostics

### Automatic topology (`main`, `retry`, `dead`)

What it does:

- Creates deterministic queue topology for each configured queue

Why it helps:

- Removes manual broker setup drift
- Standardizes operations across teams and services

### Retry (Fixed/Exponential)

What it does:

- Retries transient failures using delayed reprocessing

Why it helps:

- Absorbs temporary outages without losing messages
- Reduces manual intervention during short incidents

### Circuit breaker

What it does:

- Pauses consumption after repeated failures

Why it helps:

- Prevents cascading failures
- Reduces pressure on unstable dependencies

### Idempotency

What it does:

- Blocks duplicate processing based on message identity

Why it helps:

- Prevents double billing / duplicate side effects
- Increases safety in at-least-once delivery patterns

### Dead-letter replay

What it does:

- Re-injects dead-letter messages back to main flow

Why it helps:

- Enables controlled recovery after bug fixes
- Avoids permanent data loss from temporary business/code issues

### Throughput tuning

What it does:

- Uses workers, prefetch, and publish channel pooling

Why it helps:

- Improves throughput under constant traffic
- Reduces channel churn overhead and infrastructure cost

### Recovery diagnostics

What it does:

- Preserves root cause (`InnerException`) on recovery failures

Why it helps:

- Faster incident triage
- Lower MTTR in production environments

## 3. Which runtime mode to choose

- Hosted service: default for long-running production consumers
- Manual runtime: on-demand jobs and operational scripts
- `ConsumeManyAsync`: bounded batch processing
- `RunConsumerLoopAsync`: explicit continuous polling loops

## 4. Deployment recommendations

Single instance service:

- In-memory idempotency is often enough

Multi-instance / horizontal scale:

- Use Redis idempotency store for shared deduplication

High constant traffic:

- Increase `Workers`, `PrefetchCount`, and `PublishChannelPoolSize` gradually with load testing

## 5. Production readiness checklist

- Set explicit `messageId` on all publishes
- Keep retry and circuit breaker enabled per queue
- Expose `/health` endpoint
- Monitor retry/dead-letter/recovery trends
- Keep unit + integration tests in CI
- Keep vulnerability audit enabled in CI when feeds are stable


