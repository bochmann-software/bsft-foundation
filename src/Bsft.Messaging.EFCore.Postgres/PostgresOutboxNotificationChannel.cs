using Bsft.Messaging.EFCore.Outbox;
using Bsft.Outbox.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// The listening session of the outbox this package wakes: it picks the connection and the channel and
/// hands both to the <see cref="IPostgresListenSession"/> that does the listening. Every decision, when
/// to reconnect, what to drain, what to log, lives in <see cref="OutboxNotificationWorker"/>.
/// </summary>
/// <typeparam name="TContext">The application's DbContext type, read for its connection string.</typeparam>
/// <remarks>
/// The connection is the listener's own, taken from the pool of the connection string it is given. A
/// listening connection is held for the lifetime of the host and idle almost all of it, so it occupies one
/// pool slot for good: give the listener a different connection string, or <c>Pooling=false</c>, to keep it
/// out of the request pool.
/// </remarks>
internal sealed class PostgresOutboxNotificationChannel<TContext> : IOutboxNotificationChannel
    where TContext : DbContext, IOutboxDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BsftMessagingPostgresOptions> _options;
    private readonly IPostgresListenSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresOutboxNotificationChannel{TContext}"/> class.
    /// </summary>
    /// <param name="scopeFactory">Used to resolve the scoped context the connection string is read from.</param>
    /// <param name="options">The options carrying the channel and the optional connection string.</param>
    /// <param name="session">The session that opens the connection and listens.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public PostgresOutboxNotificationChannel(
        IServiceScopeFactory scopeFactory, IOptions<BsftMessagingPostgresOptions> options, IPostgresListenSession session)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc />
    public async Task ListenAsync(Func<CancellationToken, Task> onNotified, CancellationToken cancellationToken = default)
    {
        if (onNotified is null)
            throw new ArgumentNullException(nameof(onNotified));

        var settings = _options.Value;

        await _session
            .ListenAsync(ResolveConnectionString(settings), new PostgresNotificationChannel(settings.NotificationChannel), onNotified, cancellationToken)
            .ConfigureAwait(false);
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

        var connectionString = context.Database.GetConnectionString();

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The outbox listener found no connection string: '{typeof(TContext).Name}' does not carry one, and {nameof(BsftMessagingPostgresOptions)}.{nameof(BsftMessagingPostgresOptions.ConnectionString)} is not set.")
            : connectionString;
    }
}
