# Architecture

## 1. Purpose
EasyRabbitMQ is a .NET messaging infrastructure library that standardizes resilient RabbitMQ usage across applications.

Primary objectives:
- provide a simple, safe integration surface (`AddEasyRabbitMQ` + handlers)
- enforce fail-fast configuration validation
- standardize retry/dead-letter/circuit-breaker behavior
- support high-throughput constant traffic with channel reuse strategies

## 2. Scope
In scope:
- connection setup and recovery
- queue topology management (`main`, `retry`, `dead`)
- message publish/consume runtime
- idempotency layer (in-memory or Redis)
- DI integration, hosted consumption, health checks, telemetry hooks

Out of scope:
- business-level serialization contracts
- producer-side schema management
- cross-service workflow orchestration

## 3. System Context (C4 - Level 1)
- Applications use EasyRabbitMQ as an internal infrastructure dependency.
- EasyRabbitMQ communicates with RabbitMQ broker and optional Redis.
- Health/telemetry integrates with host platform observability stack.

External dependencies:
- RabbitMQ broker (required)
- Redis (optional; only for distributed idempotency)

## 4. Container View (C4 - Level 2)
Main runtime container: `EasyRabbitMQ` library

Submodules:
- `Configuration`: strongly-typed settings + fail-fast validator
- `DependencyInjection`: service registration and extension methods
- `Runtime`: publish/consume engine and recovery logic
- `Resilience`: retry delay calculation
- `Topology`: deterministic naming and topology model
- `Idempotency`: deduplication stores (memory/Redis)
- `Hosting`: background consumer integration
- `HealthChecks`: runtime health check entrypoint
- `Observability`: metrics/tracing counters and activity source

## 5. Component Responsibilities

### 5.1 Configuration
- `EasyRabbitMQSettingsValidator` validates required fields and prevents invalid startup.
- throws `EasyRabbitConfigurationException` for invalid config.

### 5.2 Runtime
`EasyRabbitRuntime` responsibilities:
- initialize and recover connection
- declare topology per queue
- publish messages
  - using publish channel pooling (`PublishChannelPoolSize`) for lower overhead under constant traffic
- consume messages
  - one-shot consume (`ConsumeOnceAsync`)
  - batch consume (`ConsumeManyAsync`) with global max budget control
  - dedicated long-running consumer loop (`RunConsumerLoopAsync`) using a dedicated channel per loop
- retry/dead-letter routing
- dead-letter replay

### 5.3 Idempotency
- `InMemoryIdempotencyStore`: local-process deduplication with throttled cleanup to reduce overhead under constant traffic
- `RedisIdempotencyStore`: multi-instance deduplication using shared Redis keys

### 5.4 Hosting
- `EasyRabbitConsumerHostedService` binds handlers and runs continuous consume loops.

### 5.5 Health & Telemetry
- `EasyRabbitHealthCheck`: validates runtime connectivity/topology reachability.
- `EasyRabbitTelemetry`: activity source + counters for publish/process/retry/dead-letter/recovery paths.

## 6. Runtime Flows

### 6.1 Startup Flow
1. `AddEasyRabbitMQ` binds config.
2. Validator executes fail-fast checks.
3. Runtime initializes connection and declares topology.

### 6.2 Publish Flow
1. Request routed to runtime publish API.
2. Runtime rents channel from publish pool (or creates if pool empty).
3. Message published to main exchange.
4. Channel returned to pool if healthy.

### 6.3 Consume Failure Flow
1. Handler throws exception.
2. Runtime increments attempt metadata.
3. If attempts remain: message sent to `.retry` queue with TTL.
4. If attempts exceeded: message sent to `.dead` queue.
5. Circuit breaker state updated on consecutive failures.

### 6.4 Circuit Breaker Flow
1. Consecutive failures reach threshold.
2. Queue enters open circuit for configured duration.
3. Consumption returns `CircuitOpen` during open window.
4. Automatic resume after break duration.

## 7. Non-Functional Characteristics
- Reliability: recovery for recoverable broker failures with root-cause exception chaining
- Safety: startup validation + bounded retry behavior
- Performance: worker/prefetch tuning + publish channel pooling
- Scalability: distributed idempotency via Redis
- Operability: health checks + telemetry counters

## 8. Architecture Decisions (Current)
- Decision: fail-fast config validation at startup
  - Rationale: avoid undefined behavior in production
- Decision: auto topology naming convention
  - Rationale: deterministic, convention-based operations
- Decision: publish channel pool + dedicated consumer loop channels
  - Rationale: maintain thread safety while reducing channel churn overhead
- Decision: optional Redis idempotency
  - Rationale: support both single-node and distributed deployments
- Decision: preserve inner exception on recovery failure
  - Rationale: improve production diagnostics and reduce MTTR
- Decision: throttled in-memory idempotency cleanup
  - Rationale: avoid cleanup overhead on every message under high traffic

## 9. Risks and Mitigations
- Risk: misconfigured throughput values can overload consumers
  - Mitigation: tuning guidelines + validator checks for non-zero values
- Risk: excessive retry policy can increase broker pressure
  - Mitigation: max attempts + delay caps + dead-letter routing
- Risk: Redis outage impacts distributed idempotency behavior
  - Mitigation: fallback option to in-memory mode where acceptable

## 10. Suggested Evolution
- Add benchmark suite for publish pool sizing recommendations
- Add architectural decision records (ADR folder)
- Add sequence diagrams for publish and failure paths
- Add CI quality gates (coverage threshold, package vulnerability policy)


