namespace EasyRabbitMQ.Configuration;

public sealed class EasyRabbitConfigurationException : Exception
{
    public EasyRabbitConfigurationException(string message)
        : base(message)
    {
    }

    public EasyRabbitConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
