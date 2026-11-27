using System.Threading.Channels;

namespace Bsft.Retention.Tests.Fakes;

/// <summary>
/// A retention pass whose every batch blocks until the test releases it, and which hands the test the
/// cancellation token each batch was given.
/// </summary>
/// <remarks>
/// It is what lets a test stop the sweeper while a batch is provably in flight, and then see whether
/// that batch's token was cancelled, without a real delay anywhere.
/// </remarks>
internal sealed class GatedRetentionPass : IRetentionPass
{
    private readonly Channel<CancellationToken> _batches = Channel.CreateUnbounded<CancellationToken>();
    private readonly Channel<int> _results = Channel.CreateUnbounded<int>();
    private int _calls;

    /// <summary>Gets a value indicating whether a batch keeps waiting for its release when its token is cancelled.</summary>
    public bool IgnoresCancellation { get; init; }

    /// <summary>
    /// Gets a callback registered on every batch's token, standing in for a store's command
    /// cancellation, which may block or fail.
    /// </summary>
    public Action? OnCancel { get; init; }

    /// <summary>
    /// Gets a failure a batch ends in once its token is cancelled, instead of an
    /// <see cref="OperationCanceledException"/>: the way SqlClient reports a cancelled command.
    /// </summary>
    public Exception? FailureOnCancel { get; init; }

    /// <summary>
    /// Gets a value indicating whether a released batch checks its token with
    /// <see cref="CancellationToken.ThrowIfCancellationRequested"/> before it returns, the way a pass
    /// that checks between steps does.
    /// </summary>
    public bool ChecksTokenOnRelease { get; init; }

    /// <summary>Gets how many batches the sweeper has started.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Waits for the next batch the sweeper starts.</summary>
    /// <returns>The cancellation token that batch was given.</returns>
    public async Task<CancellationToken> NextBatchAsync() =>
        await _batches.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>Lets the batch in flight, or the next one, complete.</summary>
    /// <param name="purged">How many records the batch reports it deleted.</param>
    public void Release(int purged) => _results.Writer.TryWrite(purged);

    public async Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);

        if (OnCancel is { } onCancel)
            cancellationToken.Register(onCancel);

        _batches.Writer.TryWrite(cancellationToken);

        try
        {
            var purged = await _results.Reader.ReadAsync(IgnoresCancellation ? CancellationToken.None : cancellationToken);

            if (ChecksTokenOnRelease)
                cancellationToken.ThrowIfCancellationRequested();

            return purged;
        }
        catch (OperationCanceledException) when (FailureOnCancel is { } failure)
        {
            throw failure;
        }
    }
}
