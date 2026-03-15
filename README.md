# EasyRabbitMQ

EasyRabbitMQ is a .NET 8/9 infrastructure library for RabbitMQ.Client v7+ focused on resilient messaging with practical production defaults.

## What you can do with this library

- Validate configuration at startup (fail-fast)
- Auto-create queue topology (`main`, `retry`, `dead`)
- Publish and consume with retry/dead-letter flow
- Use fixed or exponential retry
- Apply queue-level circuit breaker
- Enable idempotency (in-memory or Redis)
- Replay dead-letter messages
- Expose health checks
- Tune throughput for constant traffic (workers, prefetch, publish channel pool)
- Preserve root cause in recovery exceptions for faster troubleshooting

## Documentation

Start here:

1. [Quick Start](doc/quick-start.md)
2. [Implementation Guide](doc/implementation-guide.md)
3. [Configuration Guide](doc/configuration-guide.md)
4. [Runtime API Guide](doc/runtime-api.md)
5. [Operations Guide](doc/operations-guide.md)
6. [Architecture](doc/architecture.md)
7. [Dependencies](doc/dependencies.md)

## Minimal setup snippet

```csharp
builder.Services.AddEasyRabbitMQ(builder.Configuration);
builder.Services.AddEasyRabbitMQHostedConsumers();
```

## Project reference

```xml
<ProjectReference Include="..\\..\\src\\EasyRabbitMQ\\EasyRabbitMQ.csproj" />
```
