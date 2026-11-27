using Bsft.Outbox.Postgres;

namespace Bsft.Messaging.EFCore.Postgres.Tests.Fakes;

/// <summary>A listening session that records what it was asked to listen on and calls back once.</summary>
public sealed class RecordingListenSession : IPostgresListenSession
{
    /// <summary>Gets the connection string of the last session.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Gets the channel of the last session.</summary>
    public string? Channel { get; private set; }

    /// <summary>Gets how many times the callback was handed over and run.</summary>
    public int Callbacks { get; private set; }

    /// <inheritdoc />
    public async Task ListenAsync(
        string connectionString,
        PostgresNotificationChannel channel,
        Func<CancellationToken, Task> onNotified,
        CancellationToken cancellationToken = default)
    {
        ConnectionString = connectionString;
        Channel = channel.Name;

        await onNotified(cancellationToken);
        Callbacks++;
    }
}
