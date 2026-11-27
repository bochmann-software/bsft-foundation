using System.Threading.Channels;

namespace Bsft.Outbox.Postgres;

/// <summary>
/// The <see cref="IOutboxWakeSignal"/> of one outbox on PostgreSQL: a latch the listening worker raises
/// and the drain worker waits on.
/// </summary>
/// <remarks>
/// At most one signal is pending: a second one before the drain worker looked says nothing the first
/// did not. Abandoning a wait does not consume the pending signal.
/// </remarks>
internal sealed class PostgresOutboxWakeSignal : IOutboxWakeSignal
{
    private readonly Channel<bool> _pending = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <inheritdoc />
    public async Task WaitAsync(CancellationToken cancellationToken = default) =>
        await _pending.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Raises the signal; a signal already pending absorbs it.</summary>
    /// <param name="cancellationToken">Unused: raising never waits.</param>
    /// <returns>A completed task, so the method can be handed to a listening session as its callback.</returns>
    public Task RaiseAsync(CancellationToken cancellationToken = default)
    {
        _pending.Writer.TryWrite(true);

        return Task.CompletedTask;
    }
}
