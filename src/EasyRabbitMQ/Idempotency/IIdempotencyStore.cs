using EasyRabbitMQ.Configuration;

namespace EasyRabbitMQ.Idempotency;

public interface IIdempotencyStore
{
    Task<bool> TryBeginProcessingAsync(
        string queueName,
        string messageId,
        IdempotencySettings settings,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        string queueName,
        string messageId,
        CancellationToken cancellationToken = default);
}
