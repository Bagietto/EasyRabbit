using EasyRabbitMQ.Topology;
using FluentAssertions;

namespace EasyRabbitMQ.UnitTests.Topology;

public class QueueNamingConventionTests
{
    [Fact]
    public void GetRetryQueueName_Should_Append_Retry_Suffix()
    {
        var result = QueueNamingConvention.GetRetryQueueName("orders");

        result.Should().Be("orders.retry");
    }

    [Fact]
    public void GetDeadLetterQueueName_Should_Append_Dead_Suffix()
    {
        var result = QueueNamingConvention.GetDeadLetterQueueName("orders");

        result.Should().Be("orders.dead");
    }

    [Fact]
    public void TopologyBuilder_Should_Return_All_Queue_Names()
    {
        var topology = TopologyBuilder.Build("orders");

        topology.MainQueue.Should().Be("orders");
        topology.RetryQueue.Should().Be("orders.retry");
        topology.DeadLetterQueue.Should().Be("orders.dead");
    }
}
