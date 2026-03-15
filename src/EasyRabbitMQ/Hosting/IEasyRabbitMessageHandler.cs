namespace EasyRabbitMQ.Hosting;

public interface IEasyRabbitMessageHandler
{
    string QueueName { get; }

    Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);
}
