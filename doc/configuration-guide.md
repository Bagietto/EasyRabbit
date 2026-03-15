# Configuration Guide

This document covers all available settings, examples, and practical usage notes.

## Full configuration example

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

## `Connection`

| Option | Type | Required | Default | Practical usage |
|---|---|---|---|---|
| `HostName` | string | Yes | `""` | RabbitMQ host address |
| `Port` | int | Yes | `5672` | AMQP port |
| `VirtualHost` | string | No | `/` | Environment isolation on broker |
| `UserName` | string | No | `guest` | Broker authentication user |
| `Password` | string | No | `guest` | Broker authentication password |
| `ClientProvidedName` | string | No | `EasyRabbitMQ` | Human-friendly connection id in RabbitMQ UI |
| `RequestedHeartbeatSeconds` | ushort | No | `30` | Faster detection of broken connections |
| `AutomaticRecoveryEnabled` | bool | No | `true` | Runtime reconnect on recoverable failures |
| `TopologyRecoveryEnabled` | bool | No | `true` | Reapply topology after reconnect |
| `PublishChannelPoolSize` | int | No | `32` | Reuse publish channels and reduce overhead |

## `Queues[]`

| Option | Type | Required | Default | Practical usage |
|---|---|---|---|---|
| `Name` | string | Yes | `""` | Base queue name |
| `Durable` | bool | No | `true` | Keep queue after broker restart |
| `AutoDelete` | bool | No | `false` | Temporary queue cleanup behavior |
| `PrefetchCount` | ushort | Yes | `10` | In-flight messages per worker channel |
| `Workers` | int | Yes | `1` | Parallel consumers for this queue |
| `Retry` | object | Yes | - | Retry policy definition |
| `CircuitBreaker` | object | No | enabled defaults | Pause consumption on repeated failures |
| `Idempotency` | object | No | disabled defaults | Duplicate message protection |

## `Retry`

| Option | Type | Required | Default | Practical usage |
|---|---|---|---|---|
| `Mode` | `Fixed` or `Exponential` | Yes | `Exponential` | Fixed or growth-based retry delay |
| `MaxAttempts` | int | Yes | `5` | Max retries before dead-letter |
| `FixedDelayMs` | int? | Fixed mode | `null` | Constant retry delay |
| `InitialDelayMs` | int? | Exponential mode | `1000` | First delay in exponential sequence |
| `MaxDelayMs` | int? | Exponential mode | `60000` | Maximum exponential delay cap |
| `Multiplier` | double? | Exponential mode | `2.0` | Exponential growth factor |

Fixed retry example:

```json
"Retry": {
  "Mode": "Fixed",
  "MaxAttempts": 4,
  "FixedDelayMs": 3000
}
```

Exponential retry example:

```json
"Retry": {
  "Mode": "Exponential",
  "MaxAttempts": 6,
  "InitialDelayMs": 1000,
  "MaxDelayMs": 120000,
  "Multiplier": 2.0
}
```

## `CircuitBreaker`

| Option | Type | Required | Default | Practical usage |
|---|---|---|---|---|
| `Enabled` | bool | No | `true` | Turn protection on/off |
| `FailureThreshold` | int | If enabled | `10` | Consecutive failures before opening circuit |
| `BreakDurationSeconds` | int | If enabled | `30` | Pause duration before retrying consume |

Useful when:

- External dependencies are unstable
- You need to prevent cascading failures
- You want controlled recovery during incidents

## `Idempotency`

| Option | Type | Required | Default | Practical usage |
|---|---|---|---|---|
| `Enabled` | bool | No | `false` | Enable duplicate prevention |
| `HeaderName` | string | If enabled | `message-id` | Header used as unique message key |
| `CacheTtlMinutes` | int | If enabled | `60` | Duplicate lock window |
| `MaxTrackedMessageIds` | int | If enabled | `10000` | Memory/key bound for tracked ids |

In-memory is useful for:

- Single-instance services
- Simpler operational footprint

Redis is useful for:

- Multi-instance deployments
- Shared deduplication across replicas
