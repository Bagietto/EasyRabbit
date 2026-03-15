using EasyRabbitMQ.Topology;

namespace EasyRabbitMQ.Runtime;

public interface IEasyRabbitRuntime : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task PublishAsync(
        string queueName,
        ReadOnlyMemory<byte> body,
        bool persistent = true,
        string? messageId = null,
        IDictionary<string, object?>? headers = null,
        CancellationToken cancellationToken = default);

    Task<MessageProcessingResult> ConsumeOnceAsync(
        string queueName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken = default);

    Task RunConsumerLoopAsync(
        string queueName,
        Func<ReadOnlyMemory<byte>, Task> handler,
        TimeSpan idleDelay,
        CancellationToken cancellationToken = default);

    Task<int> ConsumeManyAsync(
        string queueName,
        int maxMessages,
        Func<ReadOnlyMemory<byte>, Task> handler,
        CancellationToken cancellationToken = default);

    Task<int> ReplayDeadLetterAsync(
        string queueName,
        int maxMessages,
        CancellationToken cancellationToken = default);

    Task<uint> GetQueueMessageCountAsync(string queueName, CancellationToken cancellationToken = default);

    QueueTopology GetQueueTopology(string queueName);
}
