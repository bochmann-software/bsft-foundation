namespace Bsft.Outbox.Tests.Support;

/// <summary>A wake signal a test raises by hand; a signal raised before anyone waits is kept, as the contract says.</summary>
public sealed class ManualWakeSignal : IOutboxWakeSignal
{
    private readonly System.Threading.Channels.Channel<bool> _raised = System.Threading.Channels.Channel.CreateBounded<bool>(
        new System.Threading.Channels.BoundedChannelOptions(1)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
        });

    /// <summary>Raises the signal.</summary>
    public void Raise() => _raised.Writer.TryWrite(true);

    /// <inheritdoc />
    public async Task WaitAsync(CancellationToken cancellationToken = default) =>
        await _raised.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
}
