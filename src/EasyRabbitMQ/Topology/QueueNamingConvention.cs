namespace EasyRabbitMQ.Topology;

public static class QueueNamingConvention
{
    public static string GetRetryQueueName(string queueName)
    {
        EnsureQueueName(queueName);
        return $"{queueName}.retry";
    }

    public static string GetDeadLetterQueueName(string queueName)
    {
        EnsureQueueName(queueName);
        return $"{queueName}.dead";
    }

    private static void EnsureQueueName(string queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName))
        {
            throw new ArgumentException("Queue name is required.", nameof(queueName));
        }
    }
}
