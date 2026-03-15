using EasyRabbitMQ.Configuration;
using FluentAssertions;

namespace EasyRabbitMQ.UnitTests.Configuration;

public class EasyRabbitConfigurationExceptionTests
{
    [Fact]
    public void Constructor_WithInnerException_Should_Preserve_RootCause()
    {
        var rootCause = new InvalidOperationException("root-cause");

        var exception = new EasyRabbitConfigurationException("wrapper", rootCause);

        exception.InnerException.Should().BeSameAs(rootCause);
        exception.Message.Should().Be("wrapper");
    }
}
