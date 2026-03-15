using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace EasyRabbitMQ.IntegrationTests.Infrastructure;

public sealed class RabbitMqIntegrationFixture : IAsyncLifetime
{
    private const string DefaultUserName = "guest";
    private const string DefaultPassword = "guest";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername(DefaultUserName)
        .WithPassword(DefaultPassword)
        .WithPortBinding(15672, true)
        .Build();

    public string HostName => _container.Hostname;

    public int AmqpPort => _container.GetMappedPublicPort(5672);

    public int ManagementPort => _container.GetMappedPublicPort(15672);

    public Uri ManagementBaseUri => new($"http://{HostName}:{ManagementPort}/api/");

    public string UserName => DefaultUserName;

    public string Password => DefaultPassword;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WaitUntilRabbitMqIsReadyAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task RestartAsync()
    {
        await _container.StopAsync();
        await _container.StartAsync();
        await WaitUntilRabbitMqIsReadyAsync();
    }

    public async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = HostName,
            Port = AmqpPort,
            UserName = UserName,
            Password = Password
        };

        return await factory.CreateConnectionAsync(cancellationToken);
    }

    private async Task WaitUntilRabbitMqIsReadyAsync()
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < timeout)
        {
            try
            {
                await using var connection = await CreateConnectionAsync();
                await connection.DisposeAsync();
                return;
            }
            catch
            {
                await Task.Delay(300);
            }
        }

        throw new TimeoutException("RabbitMQ did not become ready after restart.");
    }
}

