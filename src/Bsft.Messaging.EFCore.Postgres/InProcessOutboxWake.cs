using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// The wake-up without the database: a save that wrote outbox rows signals this process's own listener
/// directly, instead of announcing itself through <c>pg_notify</c> on one fixed connection.
/// </summary>
/// <remarks>
/// <para>
/// This is the wake-up for a host whose tenants live in databases of their own (silo mode). A
/// <c>LISTEN</c> session hears one database, so a listener on a fixed connection string would wake for one
/// tenant and stay deaf to every other. The process that committed the rows is the one that knows about
/// them, so it wakes its own dispatcher, and the dispatcher drains every tenant the
/// <see cref="Bsft.Core.Tenancy.IBackgroundTenantSource"/> lists. Other instances find the rows through their
/// poll, which stays the guarantee exactly as it does with the database notification.
/// </para>
/// <para>
/// Signals coalesce: any number of saves before the listener gets round to them cost one drain, which is
/// all they need, because a drain runs until the outbox is empty. A save inside a transaction that commits
/// later can wake the drain before its rows are visible; the drain then finds nothing, and the rows wait for
/// the next save's wake-up or the poll, the same fallback a lost database notification has.
/// </para>
/// </remarks>
internal sealed class InProcessOutboxWake : IOutboxNotifier, IOutboxNotificationChannel
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public void Notify(DbContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        _signals.Writer.TryWrite(true);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public Task NotifyAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        Notify(context);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ListenAsync(Func<CancellationToken, Task> onNotified, CancellationToken cancellationToken = default)
    {
        if (onNotified is null)
            throw new ArgumentNullException(nameof(onNotified));

        // The first drain covers what was written before the listener started, as the database session does.
        await onNotified(cancellationToken).ConfigureAwait(false);

        // The channel is never completed, so the loop ends only with the cancellation the read throws.
        while (true)
        {
            await _signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            await onNotified(cancellationToken).ConfigureAwait(false);
        }
    }
}
