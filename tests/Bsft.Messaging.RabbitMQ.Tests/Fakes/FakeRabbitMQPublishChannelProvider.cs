using Bsft.Messaging.RabbitMQ.Publishing;

namespace Bsft.Messaging.RabbitMQ.Tests.Fakes;

/// <summary>A channel provider that hands out one prepared channel, or fails as an outage would.</summary>
internal sealed class FakeRabbitMQPublishChannelProvider : IRabbitMQPublishChannelProvider
{
    public FakeRabbitMQChannel Channel { get; } = new();

    public int DisposeCount { get; private set; }

    /// <summary>Set to have getting a channel fail, as an unreachable broker does.</summary>
    public Exception? GetChannelFailure { get; set; }

    public Task<IRabbitMQChannel> GetChannelAsync(CancellationToken ct = default) =>
        GetChannelFailure is null
            ? Task.FromResult<IRabbitMQChannel>(Channel)
            : Task.FromException<IRabbitMQChannel>(GetChannelFailure);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
