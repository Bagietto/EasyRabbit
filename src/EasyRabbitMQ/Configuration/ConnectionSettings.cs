namespace EasyRabbitMQ.Configuration;

public sealed class ConnectionSettings
{
    public string HostName { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;

    public string VirtualHost { get; set; } = "/";

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string ClientProvidedName { get; set; } = "EasyRabbitMQ";

    public ushort RequestedHeartbeatSeconds { get; set; } = 30;

    public bool AutomaticRecoveryEnabled { get; set; } = true;

    public bool TopologyRecoveryEnabled { get; set; } = true;

    public int PublishChannelPoolSize { get; set; } = 32;
}
