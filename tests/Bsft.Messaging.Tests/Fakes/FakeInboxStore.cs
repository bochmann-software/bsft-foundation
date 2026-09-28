using Bsft.Messaging.Inbox;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>An inbox whose answers and failures the test dictates.</summary>
public sealed class FakeInboxStore : IInboxStore
{
    public bool IsNew { get; set; } = true;

    public int Begins { get; private set; }

    public int Commits { get; private set; }

    public int Abandons { get; private set; }

    public string? LastConsumer { get; private set; }

    public DateTimeOffset? LastReceivedAt { get; private set; }

    public Exception? BeginFailure { get; set; }

    public Exception? CommitFailure { get; set; }

    public Task<bool> TryBeginAsync(
        IntegrationEventMessage message, string consumer, DateTimeOffset receivedAt, CancellationToken ct = default)
    {
        Begins++;
        LastConsumer = consumer;
        LastReceivedAt = receivedAt;

        if (BeginFailure is not null)
            return Task.FromException<bool>(BeginFailure);

        return Task.FromResult(IsNew);
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        Commits++;

        return CommitFailure is not null ? Task.FromException(CommitFailure) : Task.CompletedTask;
    }

    public Task AbandonAsync(CancellationToken ct = default)
    {
        Abandons++;
        return Task.CompletedTask;
    }
}
