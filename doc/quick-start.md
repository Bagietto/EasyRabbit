# Quick Start

This guide gets EasyRabbitMQ running in about 5 minutes.

## 1. Add the library reference

```xml
<ProjectReference Include="..\\..\\src\\EasyRabbitMQ\\EasyRabbitMQ.csproj" />
```

## 2. Configure `appsettings.json`

```json
{
  "EasyRabbitMQ": {
    "Connection": {
      "HostName": "localhost",
      "Port": 5672,
      "UserName": "guest",
      "Password": "guest",
      "PublishChannelPoolSize": 32
    },
    "Queues": [
      {
        "Name": "orders",
        "PrefetchCount": 20,
        "Workers": 4,
        "Retry": {
          "Mode": "Exponential",
          "MaxAttempts": 5,
          "InitialDelayMs": 1000,
          "MaxDelayMs": 60000,
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
          "CacheTtlMinutes": 60,
          "MaxTrackedMessageIds": 10000
        }
      }
    ]
  }
}
```

## 3. Register in DI

```csharp
using EasyRabbitMQ.DependencyInjection;
using EasyRabbitMQ.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEasyRabbitMQ(builder.Configuration);
builder.Services.AddEasyRabbitMQHostedConsumers();
builder.Services.AddSingleton<IEasyRabbitMessageHandler, OrdersHandler>();

builder.Services
    .AddHealthChecks()
    .AddEasyRabbitMQHealthCheck();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

## 4. Implement a handler

```csharp
using EasyRabbitMQ.Hosting;

public sealed class OrdersHandler : IEasyRabbitMessageHandler
{
    public string QueueName => "orders";

    public Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        // Business logic
        // Throw exception to trigger retry/dead-letter
        return Task.CompletedTask;
    }
}
```

## 5. Publish a message

```csharp
await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes("{\"orderId\":123}"),
    messageId: "order-123",
    cancellationToken: ct);
```

## When to use this path

- Team onboarding
- New service bootstrap
- Local proof-of-concept
