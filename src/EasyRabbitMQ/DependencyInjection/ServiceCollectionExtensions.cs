using EasyRabbitMQ.Configuration;
using EasyRabbitMQ.HealthChecks;
using EasyRabbitMQ.Hosting;
using EasyRabbitMQ.Idempotency;
using EasyRabbitMQ.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyRabbitMQ.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyRabbitMQ(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settingsSection = configuration.GetSection("EasyRabbitMQ");
        var settings = settingsSection.Get<EasyRabbitMQSettings>();

        if (settings is null)
        {
            throw new EasyRabbitConfigurationException("EasyRabbitMQ configuration section was not found.");
        }

        EasyRabbitMQSettingsValidator.Validate(settings);

        services.AddSingleton(settings);
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.TryAddSingleton<IEasyRabbitRuntime, EasyRabbitRuntime>();

        return services;
    }

    public static IServiceCollection AddEasyRabbitMQHostedConsumers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<EasyRabbitConsumerHostedService>();
        return services;
    }

    public static IServiceCollection AddEasyRabbitMQRedisIdempotencyStore(
        this IServiceCollection services,
        Action<RedisIdempotencyStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RedisIdempotencyStoreOptions();
        configure(options);

        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore>(_ => new RedisIdempotencyStore(options)));
        return services;
    }

    public static IHealthChecksBuilder AddEasyRabbitMQHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "easyrabbitmq",
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<EasyRabbitHealthCheck>(name, failureStatus, tags ?? Array.Empty<string>());
    }
}
