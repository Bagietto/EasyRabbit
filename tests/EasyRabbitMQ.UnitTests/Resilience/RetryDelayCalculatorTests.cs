using EasyRabbitMQ.Configuration;
using EasyRabbitMQ.Resilience;
using FluentAssertions;

namespace EasyRabbitMQ.UnitTests.Resilience;

public class RetryDelayCalculatorTests
{
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 4000)]
    [InlineData(4, 8000)]
    public void CalculateDelayMs_Should_Calculate_Exponential_Backoff(int attempt, int expectedMs)
    {
        var retry = new RetrySettings
        {
            Mode = RetryMode.Exponential,
            MaxAttempts = 10,
            InitialDelayMs = 1000,
            Multiplier = 2.0,
            MaxDelayMs = 60000
        };

        var result = RetryDelayCalculator.CalculateDelayMs(retry, attempt);

        result.Should().Be(expectedMs);
    }

    [Fact]
    public void CalculateDelayMs_Should_Cap_To_MaxDelay()
    {
        var retry = new RetrySettings
        {
            Mode = RetryMode.Exponential,
            MaxAttempts = 10,
            InitialDelayMs = 1000,
            Multiplier = 2.0,
            MaxDelayMs = 5000
        };

        var result = RetryDelayCalculator.CalculateDelayMs(retry, 5);

        result.Should().Be(5000);
    }

    [Fact]
    public void CalculateDelayMs_Should_Return_Fixed_Delay()
    {
        var retry = new RetrySettings
        {
            Mode = RetryMode.Fixed,
            MaxAttempts = 5,
            FixedDelayMs = 3500
        };

        var result = RetryDelayCalculator.CalculateDelayMs(retry, 3);

        result.Should().Be(3500);
    }
}
