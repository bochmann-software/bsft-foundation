using Npgsql;

namespace Bsft.Outbox.Postgres;

/// <summary>
/// The listening session of <see cref="IPostgresListenSession"/>, on a PostgreSQL connection of its own.
/// </summary>
/// <remarks>
/// The connection is the listener's own, taken from the pool of the connection string it is given. A
/// listening connection is held for the lifetime of the host and idle almost all of it, so it occupies one
/// pool slot for good: give the listener a different connection string, or <c>Pooling=false</c>, to keep it
/// out of the request pool.
/// </remarks>
public sealed class PostgresListenSession : IPostgresListenSession
{
    private const string SelectChannel = "SELECT set_config('bsft.listen_channel', @channel, false)";

    private const string ListenToChannel =
        "DO $$ BEGIN EXECUTE format('LISTEN %I', current_setting('bsft.listen_channel')); END $$";

    /// <inheritdoc />
    public async Task ListenAsync(
        string connectionString,
        PostgresNotificationChannel channel,
        Func<CancellationToken, Task> onNotified,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(onNotified);

        if (channel.Name.Length == 0)
            throw new ArgumentException("The channel is the default value; create it from a name.", nameof(channel));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The handler goes on before the LISTEN, not after the catch-up call below, and the order is
        // the whole point: everything from here on is observed. A notification arriving while the
        // subscription is still being taken out, or during the catch-up call, sets the flag and is
        // answered by the wait loop, so there is no instant at which a commit can announce itself to
        // nobody. Attaching it later would leave exactly that window, and what falls into it is work
        // that waits for a poll, the one outcome a wake-up exists to avoid.
        var notified = false;
        connection.Notification += (_, _) => notified = true;

        // LISTEN takes an identifier, which no provider parameterises, so the name travels as a value and
        // the server quotes it: the command text of both statements is constant.
        await using (var setting = new NpgsqlCommand(SelectChannel, connection))
        {
            setting.Parameters.AddWithValue("channel", channel.Name);
            await setting.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var subscribe = new NpgsqlCommand(ListenToChannel, connection))
        {
            await subscribe.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Whatever was written while nothing was listening is claimed now, with the subscription
        // already in place.
        await onNotified(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Not waited for when a notification already arrived while the subscription was taken out or
            // the catch-up call ran: it is answered at once rather than after the next message.
            while (!notified)
                await connection.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Cleared before the callback rather than after it, so a notification arriving while the
            // callback runs is answered by the next pass instead of being cleared unread.
            notified = false;

            await onNotified(cancellationToken).ConfigureAwait(false);
        }
    }
}
