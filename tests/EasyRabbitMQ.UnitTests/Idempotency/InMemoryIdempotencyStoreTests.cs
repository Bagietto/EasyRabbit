using EasyRabbitMQ.Configuration;
using EasyRabbitMQ.Idempotency;
using FluentAssertions;

namespace EasyRabbitMQ.UnitTests.Idempotency;

public class InMemoryIdempotencyStoreTests
{
    private static readonly IdempotencySettings DefaultSettings = new()
    {
        Enabled = true,
        HeaderName = "message-id",
        CacheTtlMinutes = 60,
        MaxTrackedMessageIds = 2
    };

    [Fact]
    public async Task TryBeginProcessingAsync_Should_ReturnFalse_For_Duplicate_MessageId()
    {
        var store = new InMemoryIdempotencyStore(TimeSpan.FromMilliseconds(10));

        var first = await store.TryBeginProcessingAsync("orders", "msg-1", DefaultSettings);
        var duplicate = await store.TryBeginProcessingAsync("orders", "msg-1", DefaultSettings);

        first.Should().BeTrue();
        duplicate.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_Should_Allow_MessageId_To_Be_Processed_Again()
    {
        var store = new InMemoryIdempotencyStore(TimeSpan.FromMilliseconds(10));

        await store.TryBeginProcessingAsync("orders", "msg-2", DefaultSettings);
        await store.ReleaseAsync("orders", "msg-2");

        var canProcessAgain = await store.TryBeginProcessingAsync("orders", "msg-2", DefaultSettings);

        canProcessAgain.Should().BeTrue();
    }

    [Fact]
    public async Task TryBeginProcessingAsync_Should_Evict_Oldest_When_MaxTracked_Is_Exceeded()
    {
        var store = new InMemoryIdempotencyStore(TimeSpan.FromMilliseconds(10));

        await store.TryBeginProcessingAsync("orders", "msg-1", DefaultSettings);
        await Task.Delay(20);
        await store.TryBeginProcessingAsync("orders", "msg-2", DefaultSettings);
        await Task.Delay(20);
        await store.TryBeginProcessingAsync("orders", "msg-3", DefaultSettings);

        var oldestCanBeReprocessed = await store.TryBeginProcessingAsync("orders", "msg-1", DefaultSettings);

        oldestCanBeReprocessed.Should().BeTrue();
    }
}
