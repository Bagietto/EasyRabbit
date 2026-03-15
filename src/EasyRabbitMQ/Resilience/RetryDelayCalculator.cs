using EasyRabbitMQ.Configuration;

namespace EasyRabbitMQ.Resilience;

public static class RetryDelayCalculator
{
    public static int CalculateDelayMs(RetrySettings retrySettings, int attempt)
    {
        if (retrySettings is null)
        {
            throw new ArgumentNullException(nameof(retrySettings));
        }

        if (attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be greater than zero.");
        }

        return retrySettings.Mode switch
        {
            RetryMode.Fixed => CalculateFixed(retrySettings),
            RetryMode.Exponential => CalculateExponential(retrySettings, attempt),
            _ => throw new EasyRabbitConfigurationException("Retry mode is invalid.")
        };
    }

    private static int CalculateFixed(RetrySettings settings)
    {
        var fixedDelay = settings.FixedDelayMs
            ?? throw new EasyRabbitConfigurationException("Retry.FixedDelayMs is required for Fixed mode.");

        return fixedDelay > 0
            ? fixedDelay
            : throw new EasyRabbitConfigurationException("Retry.FixedDelayMs must be greater than zero for Fixed mode.");
    }

    private static int CalculateExponential(RetrySettings settings, int attempt)
    {
        var initial = settings.InitialDelayMs
            ?? throw new EasyRabbitConfigurationException("Retry.InitialDelayMs is required for Exponential mode.");
        var multiplier = settings.Multiplier
            ?? throw new EasyRabbitConfigurationException("Retry.Multiplier is required for Exponential mode.");
        var maxDelay = settings.MaxDelayMs
            ?? throw new EasyRabbitConfigurationException("Retry.MaxDelayMs is required for Exponential mode.");

        if (initial <= 0 || multiplier <= 1.0 || maxDelay <= 0)
        {
            throw new EasyRabbitConfigurationException("Invalid Exponential retry settings.");
        }

        var exponent = attempt - 1;
        var calculated = initial * Math.Pow(multiplier, exponent);

        if (calculated > int.MaxValue)
        {
            return maxDelay;
        }

        var delay = (int)Math.Round(calculated, MidpointRounding.AwayFromZero);
        return Math.Min(delay, maxDelay);
    }
}
