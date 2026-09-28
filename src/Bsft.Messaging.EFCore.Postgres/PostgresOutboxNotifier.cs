using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Sends the outbox notification through the context that wrote the rows, as a <c>pg_notify</c> call.
/// </summary>
/// <remarks>
/// <c>pg_notify</c> rather than a <c>NOTIFY</c> statement because it takes the channel as an ordinary
/// parameter, so the name never reaches the server as text this package assembled. The empty payload is
/// deliberate: a notification says only that the outbox has something in it, and the dispatcher's lease
/// decides what that something is. Carrying row identities would put a second, weaker source of truth
/// beside the lease, and PostgreSQL caps a payload at eight kilobytes anyway.
/// </remarks>
internal sealed class PostgresOutboxNotifier : IOutboxNotifier
{
    private const string NotifySql = "SELECT pg_notify({0}, '')";

    private readonly IOptions<BsftMessagingPostgresOptions> _options;
    private readonly ILogger<PostgresOutboxNotifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresOutboxNotifier"/> class.
    /// </summary>
    /// <param name="options">The options carrying the channel to announce on.</param>
    /// <param name="logger">Logger used to report a notification that could not be sent.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public PostgresOutboxNotifier(
        IOptions<BsftMessagingPostgresOptions> options, ILogger<PostgresOutboxNotifier> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Notify(DbContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var channel = _options.Value.NotificationChannel;

        try
        {
            context.Database.ExecuteSqlRaw(NotifySql, channel);
        }
        catch (DbException failure)
        {
            Report(failure, channel);
        }
    }

    /// <inheritdoc />
    public async Task NotifyAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var channel = _options.Value.NotificationChannel;

        try
        {
            await context.Database.ExecuteSqlRawAsync(NotifySql, [channel], cancellationToken).ConfigureAwait(false);
        }
        catch (DbException failure)
        {
            Report(failure, channel);
        }
    }

    /// <summary>
    /// Reports a notification that did not go out.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error, and swallowed rather than rethrown, because the rows this was
    /// about are committed either way: what was lost is the head start, and the dispatcher's poll
    /// picks the rows up within its interval. Only a database-level failure is caught — anything else
    /// coming out of this call is a misconfiguration of the context rather than a transport hiccup, and
    /// silence would leave a host wiring the wrong provider with no wake-up and no complaint either.
    /// </remarks>
    private void Report(DbException failure, string channel) =>
        _logger.LogWarning(
            failure,
            "The outbox notification on channel {NotificationChannel} could not be sent; the committed messages are delivered by the dispatcher's next poll instead.",
            channel);
}
