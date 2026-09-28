using System.Diagnostics.CodeAnalysis;
using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// The listening session, on a PostgreSQL connection of its own. Excluded from coverage: it opens a
/// connection, subscribes, waits, and hands every notification to the callback, and none of that can
/// be reached without a live server. Every decision — when to reconnect, what to drain, what to log —
/// lives in <see cref="OutboxNotificationWorker"/>, which is covered.
/// </summary>
/// <typeparam name="TContext">The application's DbContext type, read for its connection string.</typeparam>
/// <remarks>
/// The connection is not the application's. A listening connection is checked out for the lifetime of
/// the host and idle almost all of it, so taking one out of the context's pool would remove it from
/// request traffic for good; this opens its own and leaves the pool alone.
/// </remarks>
[ExcludeFromCodeCoverage] // pure delegation to Npgsql; unreachable without a live server
internal sealed class PostgresOutboxNotificationChannel<TContext> : IOutboxNotificationChannel
    where TContext : DbContext, IOutboxDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BsftMessagingPostgresOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresOutboxNotificationChannel{TContext}"/> class.
    /// </summary>
    /// <param name="scopeFactory">Used to resolve the scoped context the connection string is read from.</param>
    /// <param name="options">The options carrying the channel and the optional connection string.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public PostgresOutboxNotificationChannel(
        IServiceScopeFactory scopeFactory, IOptions<BsftMessagingPostgresOptions> options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task ListenAsync(Func<CancellationToken, Task> onNotified, CancellationToken cancellationToken = default)
    {
        if (onNotified is null)
            throw new ArgumentNullException(nameof(onNotified));

        var settings = _options.Value;

        await using var connection = new NpgsqlConnection(ResolveConnectionString(settings));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The handler goes on before the LISTEN, not after the catch-up drain below, and the order is
        // the whole point: everything from here on is observed. A notification arriving while the
        // subscription is still being taken out, or during the catch-up drain, sets the flag and is
        // answered by the wait loop, so there is no instant at which a commit can announce itself to
        // nobody. Attaching it later would leave exactly that window, and what falls into it is a
        // message that waits for a poll — the one outcome this package exists to avoid.
        var notified = false;
        connection.Notification += (_, _) => notified = true;

        await using (var subscribe = new NpgsqlCommand(
            PostgresListenStatement.For(settings.NotificationChannel), connection))
        {
            await subscribe.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Whatever was written while nothing was listening is claimed now, with the subscription
        // already in place.
        await onNotified(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            await connection.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (!notified)
                continue;

            // Cleared before the drain rather than after it, so a notification arriving while the drain
            // runs is answered by the next pass instead of being cleared unread.
            notified = false;

            await onNotified(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the connection the listener opens: the one the options state, or the application's own.
    /// </summary>
    private string ResolveConnectionString(BsftMessagingPostgresOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
            return settings.ConnectionString;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        return context.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                $"The outbox listener found no connection string: '{typeof(TContext).Name}' does not carry one, and {nameof(BsftMessagingPostgresOptions)}.{nameof(BsftMessagingPostgresOptions.ConnectionString)} is not set.");
    }
}
