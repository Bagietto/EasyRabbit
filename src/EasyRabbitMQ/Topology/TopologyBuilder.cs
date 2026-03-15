namespace EasyRabbitMQ.Topology;

public sealed record QueueTopology(string MainQueue, string RetryQueue, string DeadLetterQueue);

public static class TopologyBuilder
{
    public static QueueTopology Build(string queueName)
    {
        return new QueueTopology(
            queueName,
            QueueNamingConvention.GetRetryQueueName(queueName),
            QueueNamingConvention.GetDeadLetterQueueName(queueName));
    }
}
