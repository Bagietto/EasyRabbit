using EasyRabbitMQ.Configuration;
using FluentAssertions;

namespace EasyRabbitMQ.UnitTests.Configuration;

public class EasyRabbitMQSettingsValidatorTests
{
    [Fact]
    public void Validate_Should_Throw_When_Queues_Are_Missing()
    {
        var settings = new EasyRabbitMQSettings
        {
            Connection = new ConnectionSettings { HostName = "localhost", Port = 5672 },
            Queues = Array.Empty<QueueSettings>()
        };

        var action = () => EasyRabbitMQSettingsValidator.Validate(settings);

        action.Should().Throw<EasyRabbitConfigurationException>()
            .WithMessage("At least one queue must be configured.");
    }

    [Fact]
    public void Validate_Should_Throw_When_Exponential_Has_Invalid_Multiplier()
    {
        var settings = BuildValidSettings();
        settings.Queues[0].Retry = new RetrySettings
        {
            Mode = RetryMode.Exponential,
            MaxAttempts = 5,
            InitialDelayMs = 1000,
            MaxDelayMs = 5000,
            Multiplier = 1.0
        };

        var action = () => EasyRabbitMQSettingsValidator.Validate(settings);

        action.Should().Throw<EasyRabbitConfigurationException>()
            .WithMessage("*Retry.Multiplier must be greater than 1.0*");
    }

    [Fact]
    public void Validate_Should_Throw_When_Duplicate_Queue_Name_Exists()
    {
        var settings = BuildValidSettings();
        settings.Queues = new List<QueueSettings>
        {
            settings.Queues[0],
            new()
            {
                Name = "ORDERS",
                PrefetchCount = 10,
                Workers = 1,
                Retry = new RetrySettings
                {
                    Mode = RetryMode.Fixed,
                    MaxAttempts = 3,
                    FixedDelayMs = 1000
                }
            }
        };

        var action = () => EasyRabbitMQSettingsValidator.Validate(settings);

        action.Should().Throw<EasyRabbitConfigurationException>()
            .WithMessage("Duplicate queue name detected: 'ORDERS'.");
    }

    [Fact]
    public void Validate_Should_Throw_When_PublishChannelPoolSize_Is_Invalid()
    {
        var settings = BuildValidSettings();
        settings.Connection.PublishChannelPoolSize = 0;

        var action = () => EasyRabbitMQSettingsValidator.Validate(settings);

        action.Should().Throw<EasyRabbitConfigurationException>()
            .WithMessage("Connection.PublishChannelPoolSize must be greater than zero.");
    }

    [Fact]
    public void Validate_Should_Pass_For_Valid_Settings()
    {
        var settings = BuildValidSettings();

        var action = () => EasyRabbitMQSettingsValidator.Validate(settings);

        action.Should().NotThrow();
    }

    private static EasyRabbitMQSettings BuildValidSettings()
    {
        return new EasyRabbitMQSettings
        {
            Connection = new ConnectionSettings
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "guest",
                Password = "guest"
            },
            Queues = new List<QueueSettings>
            {
                new()
                {
                    Name = "orders",
                    PrefetchCount = 10,
                    Workers = 2,
                    Retry = new RetrySettings
                    {
                        Mode = RetryMode.Exponential,
                        MaxAttempts = 5,
                        InitialDelayMs = 1000,
                        MaxDelayMs = 30000,
                        Multiplier = 2.0
                    },
                    CircuitBreaker = new CircuitBreakerSettings
                    {
                        Enabled = true,
                        FailureThreshold = 5,
                        BreakDurationSeconds = 10
                    }
                }
            }
        };
    }
}
