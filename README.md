# EasyRabbitMQ

## English

### Overview
EasyRabbitMQ is a .NET 8/9 infrastructure library for RabbitMQ.Client v7+.
It standardizes resilient messaging patterns with practical defaults and production-oriented behavior.

### What you can do with this library
- Validate configuration at startup (fail-fast)
- Auto-create queue topology (`main`, `retry`, `dead`)
- Publish messages with retry/dead-letter compatible metadata
- Consume in one-shot, batch, or continuous hosted mode
- Apply fixed or exponential retry delays
- Enable per-queue circuit breaker
- Enable idempotency (in-memory or Redis)
- Replay dead-letter messages
- Expose health checks
- Tune throughput for constant traffic (workers, prefetch, publish channel pool)
- Recovery failures preserve root cause (InnerException) for faster troubleshooting

---

### 1. Installation / Reference

Project reference:

```xml
<ProjectReference Include="..\\..\\src\\EasyRabbitMQ\\EasyRabbitMQ.csproj" />
```

---

### 2. Full Configuration (`appsettings.json`)

```json
{
  "EasyRabbitMQ": {
    "Connection": {
      "HostName": "localhost",
      "Port": 5672,
      "VirtualHost": "/",
      "UserName": "guest",
      "Password": "guest",
      "ClientProvidedName": "MyApp.EasyRabbit",
      "RequestedHeartbeatSeconds": 30,
      "AutomaticRecoveryEnabled": true,
      "TopologyRecoveryEnabled": true,
      "PublishChannelPoolSize": 32
    },
    "Queues": [
      {
        "Name": "orders",
        "Durable": true,
        "AutoDelete": false,
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

---

### 3. Register in DI (`Program.cs`)

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

Redis idempotency (optional):

```csharp
builder.Services.AddEasyRabbitMQRedisIdempotencyStore(options =>
{
    options.ConnectionString = "localhost:6379";
    options.KeyPrefix = "myapp:easyrabbit:idempotency";
});
```

---

### 4. Handler Implementation (Hosted Consumption)

```csharp
using EasyRabbitMQ.Hosting;

public sealed class OrdersHandler : IEasyRabbitMessageHandler
{
    public string QueueName => "orders";

    public Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var json = System.Text.Encoding.UTF8.GetString(body.Span);

        // Business logic
        // Throw exception to trigger retry/dead-letter

        return Task.CompletedTask;
    }
}
```

---

### 5. Manual Runtime Usage (without Hosted Service)

```csharp
using EasyRabbitMQ.Runtime;
using System.Text;

public sealed class MessagingService
{
    private readonly IEasyRabbitRuntime _runtime;

    public MessagingService(IEasyRabbitRuntime runtime)
    {
        _runtime = runtime;
    }

    public async Task PublishOrderAsync(string payload, CancellationToken ct)
    {
        await _runtime.InitializeAsync(ct);

        await _runtime.PublishAsync(
            queueName: "orders",
            body: Encoding.UTF8.GetBytes(payload),
            messageId: Guid.NewGuid().ToString("N"),
            cancellationToken: ct);
    }

    public async Task<MessageProcessingResult> ConsumeOnceAsync(CancellationToken ct)
    {
        return await _runtime.ConsumeOnceAsync(
            queueName: "orders",
            handler: body =>
            {
                var json = Encoding.UTF8.GetString(body.Span);
                return Task.CompletedTask;
            },
            cancellationToken: ct);
    }

    public async Task<int> ConsumeBatchAsync(CancellationToken ct)
    {
        return await _runtime.ConsumeManyAsync(
            queueName: "orders",
            maxMessages: 100,
            handler: body => Task.CompletedTask,
            cancellationToken: ct);
    }

    public async Task<int> ReplayDeadAsync(CancellationToken ct)
    {
        return await _runtime.ReplayDeadLetterAsync("orders", maxMessages: 100, cancellationToken: ct);
    }
}
```

---

### 6. Publish Examples

Publish with explicit MessageId (recommended):

```csharp
await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes(json),
    messageId: "order-2026-0001",
    cancellationToken: ct);
```

Publish with custom headers:

```csharp
await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes(json),
    headers: new Dictionary<string, object?>
    {
        ["message-id"] = "order-2026-0001",
        ["tenant"] = "acme",
        ["source"] = "checkout-api"
    },
    cancellationToken: ct);
```

---

### 7. Continuous Consumer Loop (Dedicated Channel)

```csharp
using var cts = new CancellationTokenSource();

await runtime.RunConsumerLoopAsync(
    queueName: "orders",
    handler: async body =>
    {
        var json = Encoding.UTF8.GetString(body.Span);
        await Task.CompletedTask;
    },
    idleDelay: TimeSpan.FromMilliseconds(100),
    cancellationToken: cts.Token);
```

---

### 8. CancellationToken Best Practices
- Always pass `CancellationToken` in async methods.
- Treat `OperationCanceledException` as normal shutdown flow.
- Reuse host `stoppingToken` in background consumers.

Example:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes("{\"id\":1}"),
    cancellationToken: cts.Token);
```

---

### 9. Configuration Reference

#### `Connection`
| Option | Type | Required | Default | Notes |
|---|---|---|---|---|
| `HostName` | string | Yes | `""` | RabbitMQ host |
| `Port` | int | Yes | `5672` | Must be `> 0` |
| `VirtualHost` | string | No | `/` | Broker vhost |
| `UserName` | string | No | `guest` | Login user |
| `Password` | string | No | `guest` | Login password |
| `ClientProvidedName` | string | No | `EasyRabbitMQ` | Connection display name |
| `RequestedHeartbeatSeconds` | ushort | No | `30` | Heartbeat interval |
| `AutomaticRecoveryEnabled` | bool | No | `true` | Enables auto recovery |
| `TopologyRecoveryEnabled` | bool | No | `true` | Restores topology after reconnect |
| `PublishChannelPoolSize` | int | No | `32` | Idle publish channels kept in pool |

#### `Queues[]`
| Option | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Name` | string | Yes | `""` | Base queue name |
| `Durable` | bool | No | `true` | Persistent queue |
| `AutoDelete` | bool | No | `false` | Auto-delete queue |
| `PrefetchCount` | ushort | Yes | `10` | In-flight msgs per worker channel |
| `Workers` | int | Yes | `1` | Parallel workers |
| `Retry` | object | Yes | - | Retry policy |
| `CircuitBreaker` | object | No | enabled defaults | Infra failure protection |
| `Idempotency` | object | No | disabled defaults | Duplicate protection |

#### `Retry`
| Option | Type | Required | Default |
|---|---|---|---|
| `Mode` | Fixed \| Exponential | Yes | Exponential |
| `MaxAttempts` | int | Yes | 5 |
| `FixedDelayMs` | int? | Fixed mode | null |
| `InitialDelayMs` | int? | Exponential mode | 1000 |
| `MaxDelayMs` | int? | Exponential mode | 60000 |
| `Multiplier` | double? | Exponential mode | 2.0 |

#### `CircuitBreaker`
| Option | Type | Required | Default |
|---|---|---|---|
| `Enabled` | bool | No | true |
| `FailureThreshold` | int | If enabled | 10 |
| `BreakDurationSeconds` | int | If enabled | 30 |

#### `Idempotency`
| Option | Type | Required | Default |
|---|---|---|---|
| `Enabled` | bool | No | false |
| `HeaderName` | string | If enabled | `message-id` |
| `CacheTtlMinutes` | int | If enabled | 60 |
| `MaxTrackedMessageIds` | int | If enabled | 10000 |

---

### 10. Tuning Guides

Low traffic:
```json
"PublishChannelPoolSize": 8,
"Workers": 1,
"PrefetchCount": 5
```

Balanced:
```json
"PublishChannelPoolSize": 32,
"Workers": 4,
"PrefetchCount": 20
```

Constant high traffic:
```json
"PublishChannelPoolSize": 64,
"Workers": 8,
"PrefetchCount": 50
```

---

### 11. Test Status
- Unit tests: `18 passed`
- Integration tests: `12 passed`

---

## Portugues (PT-BR)

### Visao Geral
EasyRabbitMQ e uma biblioteca de infraestrutura para .NET 8/9 com RabbitMQ.Client v7+.
Ela padroniza mensageria resiliente com foco em simplicidade e producao.

### O que a lib entrega
- validacao fail-fast no startup
- topologia automatica (`main`, `retry`, `dead`)
- retry fixo e exponencial
- circuit breaker por fila
- idempotencia (in-memory ou Redis)
- replay de dead-letter
- consumo hosted e manual
- health check
- tuning para trafego constante com pool de canais de publish
- preserva causa raiz de falhas de recovery para diagnostico mais rapido

---

### 1. Como integrar no projeto

#### 1.1 Referencia
```xml
<ProjectReference Include="..\\..\\src\\EasyRabbitMQ\\EasyRabbitMQ.csproj" />
```

#### 1.2 Configurar `appsettings.json`
Use o exemplo completo da secao em ingles.

#### 1.3 Registrar no DI (`Program.cs`)
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

Redis (opcional):
```csharp
builder.Services.AddEasyRabbitMQRedisIdempotencyStore(options =>
{
    options.ConnectionString = "localhost:6379";
    options.KeyPrefix = "myapp:easyrabbit:idempotency";
});
```

---

### 2. Exemplo de handler
```csharp
using EasyRabbitMQ.Hosting;

public sealed class OrdersHandler : IEasyRabbitMessageHandler
{
    public string QueueName => "orders";

    public Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var json = System.Text.Encoding.UTF8.GetString(body.Span);

        // Regra de negocio
        // Lance excecao para acionar retry/dead-letter

        return Task.CompletedTask;
    }
}
```

---

### 3. Uso manual da runtime
Use esse modo quando voce quiser controlar publicacao e consumo sem `HostedService`.

```csharp
using EasyRabbitMQ.Runtime;
using System.Text;

await runtime.InitializeAsync(ct);

await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes("{\"id\":1}"),
    messageId: "order-1",
    cancellationToken: ct);

var result = await runtime.ConsumeOnceAsync(
    queueName: "orders",
    handler: body => Task.CompletedTask,
    cancellationToken: ct);

var processed = await runtime.ConsumeManyAsync(
    queueName: "orders",
    maxMessages: 100,
    handler: body => Task.CompletedTask,
    cancellationToken: ct);

var replayed = await runtime.ReplayDeadLetterAsync("orders", 100, ct);
```

O que cada chamada faz:
- `InitializeAsync(ct)`: inicializa conexao e declara a topologia das filas configuradas.
- `PublishAsync(...)`: publica uma mensagem na fila principal; `messageId` ajuda na idempotencia.
- `ConsumeOnceAsync(...)`: tenta consumir 1 mensagem da fila e retorna o resultado (`Processed`, `NoMessage`, `MovedToRetry`, etc.).
- `ConsumeManyAsync(...)`: processa em lote ate `maxMessages`, usando configuracao de `Workers` e `PrefetchCount`.
- `ReplayDeadLetterAsync(...)`: move mensagens da fila `.dead` de volta para fila principal para novo processamento.

Quando usar cada uma:
- `ConsumeOnceAsync`: jobs pontuais, polling controlado, diagnostico.
- `ConsumeManyAsync`: processamento em lote com limite de volume.
- `ReplayDeadLetterAsync`: rotina de reprocessamento apos correcoes.

---

### 4. `CancellationToken` na pratica
- passe o token em todas as chamadas async
- use o `stoppingToken` no hosted service
- trate `OperationCanceledException` como desligamento normal

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await runtime.PublishAsync("orders", Encoding.UTF8.GetBytes("{}"), cancellationToken: cts.Token);
```

---

### 5. Referencia de configuracao (todas as opcoes)
A tabela de opcoes em ingles cobre todas as propriedades:
- `Connection`
- `Queues[]`
- `Retry`
- `CircuitBreaker`
- `Idempotency`

---

### 6. Receitas de tuning

Baixo trafego:
```json
"PublishChannelPoolSize": 8,
"Workers": 1,
"PrefetchCount": 5
```

Balanceado:
```json
"PublishChannelPoolSize": 32,
"Workers": 4,
"PrefetchCount": 20
```

Trafego constante alto:
```json
"PublishChannelPoolSize": 64,
"Workers": 8,
"PrefetchCount": 50
```

---

### 7. Status de testes
- Unitarios: `18 passed`
- Integracao: `12 passed`






---

### 8. Quick Start (5 minutos)

Passo 1. Adicione a referencia da biblioteca:
```xml
<ProjectReference Include="..\\..\\src\\EasyRabbitMQ\\EasyRabbitMQ.csproj" />
```

Passo 2. Configure o `appsettings.json`:
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

Passo 3. Registre no DI:
```csharp
builder.Services.AddEasyRabbitMQ(builder.Configuration);
builder.Services.AddEasyRabbitMQHostedConsumers();
builder.Services.AddSingleton<IEasyRabbitMessageHandler, OrdersHandler>();
```

Passo 4. Publique mensagem:
```csharp
await runtime.PublishAsync(
    queueName: "orders",
    body: Encoding.UTF8.GetBytes("{\"orderId\":123}"),
    messageId: "order-123",
    cancellationToken: ct);
```

Passo 5. Consuma com handler hosted:
```csharp
public sealed class OrdersHandler : IEasyRabbitMessageHandler
{
    public string QueueName => "orders";

    public Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        // Regra de negocio
        return Task.CompletedTask;
    }
}
```

Quando usar esse quick start:
- onboarding rapido do time
- bootstrap de novo servico
- prova de conceito em ambiente local

---

### 9. Guia de implementacao por cenario

#### 9.1 Hosted Service (modo recomendado)
Como implementar:
```csharp
builder.Services.AddEasyRabbitMQ(builder.Configuration);
builder.Services.AddEasyRabbitMQHostedConsumers();
builder.Services.AddSingleton<IEasyRabbitMessageHandler, OrdersHandler>();
```

Utilidade:
- consumo continuo com ciclo de vida integrado ao host
- desligamento gracioso com `stoppingToken`
- padrao ideal para APIs e workers em producao

#### 9.2 Runtime manual (controle total)
Como implementar:
```csharp
await runtime.InitializeAsync(ct);
await runtime.PublishAsync("orders", body, cancellationToken: ct);
await runtime.ConsumeOnceAsync("orders", handler, ct);
```

Utilidade:
- jobs sob demanda
- fluxos de manutencao e scripts operacionais
- testes de comportamento especifico de consumo/publicacao

#### 9.3 Consumo em lote (`ConsumeManyAsync`)
Como implementar:
```csharp
var processed = await runtime.ConsumeManyAsync(
    queueName: "orders",
    maxMessages: 500,
    handler: ProcessAsync,
    cancellationToken: ct);
```

Utilidade:
- drenar fila com limite controlado
- janelas de processamento periodico
- tarefas em lote sem loop infinito

#### 9.4 Loop dedicado (`RunConsumerLoopAsync`)
Como implementar:
```csharp
await runtime.RunConsumerLoopAsync(
    queueName: "orders",
    handler: ProcessAsync,
    idleDelay: TimeSpan.FromMilliseconds(100),
    cancellationToken: ct);
```

Utilidade:
- polling continuo com canal dedicado
- menor overhead para carga constante
- controle explicito do ritmo de polling

#### 9.5 Replay de Dead Letter
Como implementar:
```csharp
var replayed = await runtime.ReplayDeadLetterAsync("orders", maxMessages: 1000, cancellationToken: ct);
```

Utilidade:
- reprocessar mensagens apos correcao de bug
- operacoes de recuperacao orientadas por limite
- evitar perda de mensagens que falharam definitivamente

#### 9.6 Idempotencia Redis (distribuido)
Como implementar:
```csharp
builder.Services.AddEasyRabbitMQRedisIdempotencyStore(options =>
{
    options.ConnectionString = "localhost:6379";
    options.KeyPrefix = "myapp:easyrabbit:idempotency";
});
```

Utilidade:
- deduplicacao compartilhada entre varias instancias
- ideal para escalonamento horizontal
- reduz risco de processamento duplicado em cluster

#### 9.7 Health Check
Como implementar:
```csharp
builder.Services.AddHealthChecks().AddEasyRabbitMQHealthCheck();
app.MapHealthChecks("/health");
```

Utilidade:
- readiness/liveness para Kubernetes/App Service
- deteccao antecipada de problema de conectividade
- observabilidade operacional padrao de mercado

---

### 10. Guia completo de opcoes e utilidade

#### 10.1 Connection
| Opcao | Como configurar | Para que serve na pratica |
|---|---|---|
| `HostName` | `"HostName": "rabbitmq"` | Endereco do broker RabbitMQ. |
| `Port` | `"Port": 5672` | Porta AMQP usada pela conexao. |
| `VirtualHost` | `"VirtualHost": "/"` | Isolamento logico de ambientes no broker. |
| `UserName` | `"UserName": "guest"` | Usuario de autenticacao. |
| `Password` | `"Password": "guest"` | Senha do usuario. |
| `ClientProvidedName` | `"ClientProvidedName": "Orders.Api"` | Nome amigavel da conexao para troubleshooting no RabbitMQ UI. |
| `RequestedHeartbeatSeconds` | `"RequestedHeartbeatSeconds": 30` | Detecta conexoes quebradas mais rapidamente. |
| `AutomaticRecoveryEnabled` | `"AutomaticRecoveryEnabled": true` | Tenta recuperar conexao automaticamente em falhas de infra. |
| `TopologyRecoveryEnabled` | `"TopologyRecoveryEnabled": true` | Reaplica topologia apos reconexao. |
| `PublishChannelPoolSize` | `"PublishChannelPoolSize": 32` | Reuso de canais de publish para reduzir overhead em trafego constante. |

#### 10.2 Queues[]
| Opcao | Como configurar | Para que serve na pratica |
|---|---|---|
| `Name` | `"Name": "orders"` | Nome base da fila principal. |
| `Durable` | `"Durable": true` | Mantem fila apos restart do broker. |
| `AutoDelete` | `"AutoDelete": false` | Remove fila quando nao ha consumidores (casos temporarios). |
| `PrefetchCount` | `"PrefetchCount": 20` | Quantas mensagens cada worker pega por vez. |
| `Workers` | `"Workers": 4` | Quantidade de consumidores paralelos para a fila. |
| `Retry` | objeto `Retry` | Define politica de retentativa. |
| `CircuitBreaker` | objeto `CircuitBreaker` | Pausa consumo apos falhas consecutivas. |
| `Idempotency` | objeto `Idempotency` | Evita processamento duplicado. |

#### 10.3 Retry
| Opcao | Como configurar | Para que serve na pratica |
|---|---|---|
| `Mode` | `"Mode": "Fixed"` ou `"Exponential"` | Escolhe atraso fixo ou progressivo entre tentativas. |
| `MaxAttempts` | `"MaxAttempts": 5` | Limite de tentativas antes de enviar para `.dead`. |
| `FixedDelayMs` | `"FixedDelayMs": 5000` | Intervalo fixo entre tentativas (modo Fixed). |
| `InitialDelayMs` | `"InitialDelayMs": 1000` | Atraso inicial (modo Exponential). |
| `MaxDelayMs` | `"MaxDelayMs": 60000` | Teto de atraso no exponential backoff. |
| `Multiplier` | `"Multiplier": 2.0` | Fator de crescimento do atraso exponencial. |

Exemplo de retry fixo:
```json
"Retry": {
  "Mode": "Fixed",
  "MaxAttempts": 4,
  "FixedDelayMs": 3000
}
```

Exemplo de retry exponencial:
```json
"Retry": {
  "Mode": "Exponential",
  "MaxAttempts": 6,
  "InitialDelayMs": 1000,
  "MaxDelayMs": 120000,
  "Multiplier": 2.0
}
```

#### 10.4 CircuitBreaker
| Opcao | Como configurar | Para que serve na pratica |
|---|---|---|
| `Enabled` | `"Enabled": true` | Ativa protecao contra falhas repetidas de infraestrutura. |
| `FailureThreshold` | `"FailureThreshold": 5` | Numero de falhas consecutivas para abrir circuito. |
| `BreakDurationSeconds` | `"BreakDurationSeconds": 20` | Tempo de pausa antes de tentar consumir novamente. |

Quando e util:
- indisponibilidade temporaria de dependencia externa
- evitar efeito cascata em periodos de instabilidade
- proteger broker e aplicacao durante incidentes

#### 10.5 Idempotency
| Opcao | Como configurar | Para que serve na pratica |
|---|---|---|
| `Enabled` | `"Enabled": true` | Liga protecao contra duplicidade. |
| `HeaderName` | `"HeaderName": "message-id"` | Cabecalho usado para identificar mensagem unica. |
| `CacheTtlMinutes` | `"CacheTtlMinutes": 60` | Janela de tempo para bloquear reprocessamento duplicado. |
| `MaxTrackedMessageIds` | `"MaxTrackedMessageIds": 10000` | Limita memoria/chaves monitoradas para deduplicacao. |

Quando usar in-memory:
- servico unico ou baixa escala
- simplicidade operacional

Quando usar Redis:
- multiplas replicas do mesmo servico
- necessidade de deduplicacao compartilhada entre instancias

---

### 11. Checklist de implementacao para producao

- Configurar `AutomaticRecoveryEnabled`, `Retry` e `CircuitBreaker` por fila.
- Definir `MessageId` em toda publicacao.
- Habilitar idempotencia (`in-memory` ou `Redis`) conforme topologia de deploy.
- Expor `health check` para readiness/liveness.
- Ajustar `Workers`, `PrefetchCount` e `PublishChannelPoolSize` com teste de carga.
- Monitorar metricas de `retry`, `dead-letter` e `recovery`.
- Manter `dotnet test` unitario e integracao no pipeline.
