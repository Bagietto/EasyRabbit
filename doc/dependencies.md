# Dependencies

## 1. Dependency Strategy
This project uses direct, pinned package versions to maximize build reproducibility and reduce supply-chain surprises.

Principles:
- pin direct dependency versions (no wildcard versions)
- keep production and test dependency sets separated
- run restore/build/test in CI for every change
- regularly review for security and update cadence

## 2. Dependency Inventory

### 2.1 Production (`src/EasyRabbitMQ/EasyRabbitMQ.csproj`)
| Package | Version | Category | Why it exists |
|---|---|---|---|
| Microsoft.Extensions.Configuration.Abstractions | 9.0.0 | Runtime | Configuration access contracts |
| Microsoft.Extensions.Configuration.Binder | 9.0.0 | Runtime | Binds `appsettings` into typed settings |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.0 | Runtime | DI extension surfaces |
| Microsoft.Extensions.Diagnostics.HealthChecks | 9.0.0 | Runtime | Health check integration |
| Microsoft.Extensions.Hosting.Abstractions | 9.0.0 | Runtime | Hosted service contracts |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | Runtime | Logging contracts |
| RabbitMQ.Client | 7.2.1 | Core messaging | AMQP client for publish/consume |
| StackExchange.Redis | 2.8.37 | Optional infrastructure | Distributed idempotency store |

### 2.2 Unit tests (`tests/EasyRabbitMQ.UnitTests`)
| Package | Version | Category | Why it exists |
|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.12.0 | Test runner | .NET test host |
| xunit | 2.9.2 | Test framework | Unit test framework |
| xunit.runner.visualstudio | 2.8.2 | Test runner adapter | VS/test discovery integration |
| FluentAssertions | 8.8.0 | Test utility | Readable assertions |
| coverlet.collector | 6.0.2 | Coverage | Code coverage collection |

### 2.3 Integration tests (`tests/EasyRabbitMQ.IntegrationTests`)
| Package | Version | Category | Why it exists |
|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.12.0 | Test runner | .NET test host |
| xunit | 2.9.2 | Test framework | Integration tests |
| xunit.runner.visualstudio | 2.8.2 | Test runner adapter | VS/test discovery integration |
| FluentAssertions | 8.8.0 | Test utility | Assertion helpers |
| coverlet.collector | 6.0.2 | Coverage | Code coverage collection |
| RabbitMQ.Client | 7.2.1 | Integration runtime | Broker interaction in tests |
| Testcontainers.RabbitMq | 4.11.0 | Integration infra | Ephemeral RabbitMQ container |
| Polly | 7.2.4 | Test resilience helper | Retry/wait orchestration in test scenarios |

## 3. Build and Packaging Settings
The production project is configured for package quality and deterministic outputs:
- `TreatWarningsAsErrors = true`
- `Deterministic = true`
- `ContinuousIntegrationBuild = true`
- NuGet metadata (`PackageId`, `Version`, `RepositoryUrl`, license, symbols)

## 4. Security and Maintenance Policy
Recommended operational policy:
1. Run dependency vulnerability scans in CI (`dotnet list package --vulnerable`).
2. Review dependency updates at least monthly.
3. Prefer patch/minor updates first, then major updates with explicit migration validation.
4. Re-run full unit + integration test suites before merging dependency upgrades.

## 5. Upgrade Playbook
1. Update one dependency group at a time (runtime first, then tests).
2. Execute:
   - `dotnet restore`
   - `dotnet build`
   - `dotnet test`
3. Validate broker scenarios:
   - retry/dead-letter flow
   - circuit breaker behavior
   - recovery after broker restart
   - idempotency behavior
4. Update documentation and changelog with notable impact.

## 6. Optional Additions (Recommended)
- Add Dependabot or Renovate for controlled update PRs.
- Add SBOM generation in CI (`dotnet sbom` or equivalent toolchain).
- Add signed package verification policy for internal feeds.

## 7. Known Optional Runtime Dependencies
- Redis is optional and only required when using distributed idempotency.
- Without Redis configuration, in-memory idempotency is used.

## 8. Decision Notes
- Wildcard versions were intentionally removed to avoid non-deterministic restores.
- Package set is intentionally minimal to keep operational surface area low.


## 9. NuGet vulnerability audit caveat
In some environments, private package feeds may block vulnerability metadata retrieval and fail restore/test when warnings are treated as errors.
Operational guidance:
- keep NuGetAudit enabled in CI environments with proper feed access
- if a feed is temporarily unavailable, use /p:NuGetAudit=false only as a temporary unblock step
- track this as technical debt and restore full audit as soon as feed connectivity is stable

