namespace EasyRabbitMQ.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class RabbitMqIntegrationCollection : ICollectionFixture<RabbitMqIntegrationFixture>
{
    public const string Name = "rabbitmq-integration";
}
