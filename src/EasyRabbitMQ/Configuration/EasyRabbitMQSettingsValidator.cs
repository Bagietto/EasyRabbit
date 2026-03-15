using System.Collections.ObjectModel;

namespace EasyRabbitMQ.Configuration;

public static class EasyRabbitMQSettingsValidator
{
    public static void Validate(EasyRabbitMQSettings settings)
    {
        if (settings is null)
        {
            throw new EasyRabbitConfigurationException("EasyRabbitMQ settings section is missing.");
        }

        if (settings.Connection is null)
        {
            throw new EasyRabbitConfigurationException("Connection settings are required.");
        }

        if (string.IsNullOrWhiteSpace(settings.Connection.HostName))
        {
            throw new EasyRabbitConfigurationException("Connection.HostName is required.");
        }

        if (settings.Connection.Port <= 0)
        {
            throw new EasyRabbitConfigurationException("Connection.Port must be greater than zero.");
        }

        if (settings.Connection.PublishChannelPoolSize <= 0)
        {
            throw new EasyRabbitConfigurationException("Connection.PublishChannelPoolSize must be greater than zero.");
        }

        if (settings.Queues is null || settings.Queues.Count == 0)
        {
            throw new EasyRabbitConfigurationException("At least one queue must be configured.");
        }

        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var queue in settings.Queues)
        {
            if (string.IsNullOrWhiteSpace(queue.Name))
            {
                throw new EasyRabbitConfigurationException("Queue.Name is required.");
            }

            if (!uniqueNames.Add(queue.Name))
            {
                throw new EasyRabbitConfigurationException($"Duplicate queue name detected: '{queue.Name}'.");
            }

            if (queue.PrefetchCount == 0)
            {
                throw new EasyRabbitConfigurationException($"Queue '{queue.Name}': PrefetchCount must be greater than zero.");
            }

            if (queue.Workers <= 0)
            {
                throw new EasyRabbitConfigurationException($"Queue '{queue.Name}': Workers must be greater than zero.");
            }

            ValidateRetry(queue.Name, queue.Retry);
            ValidateCircuitBreaker(queue.Name, queue.CircuitBreaker);
            ValidateIdempotency(queue.Name, queue.Idempotency);
        }
    }

    private static void ValidateRetry(string queueName, RetrySettings? retry)
    {
        if (retry is null)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Retry settings are required.");
        }

        if (retry.MaxAttempts <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Retry.MaxAttempts must be greater than zero.");
        }

        if (retry.Mode == RetryMode.Fixed)
        {
            if (retry.FixedDelayMs is null || retry.FixedDelayMs <= 0)
            {
                throw new EasyRabbitConfigurationException($"Queue '{queueName}': Retry.FixedDelayMs must be greater than zero for Fixed mode.");
            }

            return;
        }

        if (retry.InitialDelayMs is null || retry.InitialDelayMs <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Retry.InitialDelayMs must be greater than zero for Exponential mode.");
        }

        if (retry.Multiplier is null || retry.Multiplier <= 1.0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Retry.Multiplier must be greater than 1.0 for Exponential mode.");
        }

        if (retry.MaxDelayMs is null || retry.MaxDelayMs <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Retry.MaxDelayMs must be greater than zero for Exponential mode.");
        }
    }

    private static void ValidateIdempotency(string queueName, IdempotencySettings? idempotency)
    {
        if (idempotency is null || !idempotency.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(idempotency.HeaderName))
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Idempotency.HeaderName is required when Idempotency.Enabled is true.");
        }

        if (idempotency.CacheTtlMinutes <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Idempotency.CacheTtlMinutes must be greater than zero.");
        }

        if (idempotency.MaxTrackedMessageIds <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': Idempotency.MaxTrackedMessageIds must be greater than zero.");
        }
    }

    private static void ValidateCircuitBreaker(string queueName, CircuitBreakerSettings? circuitBreaker)
    {
        if (circuitBreaker is null || !circuitBreaker.Enabled)
        {
            return;
        }

        if (circuitBreaker.FailureThreshold <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': CircuitBreaker.FailureThreshold must be greater than zero.");
        }

        if (circuitBreaker.BreakDurationSeconds <= 0)
        {
            throw new EasyRabbitConfigurationException($"Queue '{queueName}': CircuitBreaker.BreakDurationSeconds must be greater than zero.");
        }
    }
}
