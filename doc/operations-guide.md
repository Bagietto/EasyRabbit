# Operations Guide

This document covers production operations, tuning, and validation.

## Throughput tuning profiles

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

## Production checklist

- Enable `AutomaticRecoveryEnabled` and queue-level `Retry` + `CircuitBreaker`.
- Set `messageId` on every publish.
- Enable idempotency (`in-memory` or Redis based on deployment model).
- Expose health checks for readiness/liveness.
- Tune `Workers`, `PrefetchCount`, and `PublishChannelPoolSize` with load tests.
- Monitor retry/dead-letter/recovery metrics.
- Run unit and integration tests in CI.

## Health and observability

Recommended:

- Health endpoint mapped (for orchestrators and probes)
- Metrics collection from runtime counters
- Error logs with preserved root cause (`InnerException`) for recovery failures

## Test status baseline

Current expected baseline:

- Unit tests: `18 passed`
- Integration tests: `12 passed`

## CI note for package vulnerability audit

If private feeds are temporarily unavailable, `NuGetAudit` may fail restore/test.
Preferred approach:

- Keep `NuGetAudit` enabled in CI with stable feed access.
- Use `/p:NuGetAudit=false` only as a temporary unblock step.
- Track and remove this workaround as soon as feed connectivity is restored.
