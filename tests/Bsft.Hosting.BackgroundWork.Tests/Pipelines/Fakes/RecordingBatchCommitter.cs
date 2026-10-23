using Bsft.Hosting.BackgroundWork.Pipelines;
using FluentAssertions;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;

/// <summary>A committer whose every batch is recorded and whose behavior a test scripts per batch.</summary>
internal sealed class RecordingBatchCommitter : IBatchCommitter<SampleItem>
{
    private readonly List<IReadOnlyList<SampleItem>> _batches = [];
    private readonly SemaphoreSlim _started = new(0);

    /// <summary>Gets or sets what a batch does once it is recorded; the argument is its one-based number.</summary>
    public Func<int, CancellationToken, Task> Behavior { get; set; } = static (_, _) => Task.CompletedTask;

    /// <summary>Gets the batches received so far, in order.</summary>
    public IReadOnlyList<IReadOnlyList<SampleItem>> Batches
    {
        get
        {
            lock (_batches)
                return _batches.ToArray();
        }
    }

    /// <summary>Gets every item received so far, in order.</summary>
    public IReadOnlyList<SampleItem> Items => Batches.SelectMany(batch => batch).ToArray();

    /// <inheritdoc />
    public Task CommitAsync(IReadOnlyList<SampleItem> batch, CancellationToken cancellationToken)
    {
        int number;
        lock (_batches)
        {
            _batches.Add(batch.ToArray());
            number = _batches.Count;
        }

        _started.Release();
        return Behavior(number, cancellationToken);
    }

    /// <summary>Waits until <paramref name="count"/> batches have started.</summary>
    /// <param name="count">How many batches.</param>
    /// <returns>A task that completes when they have.</returns>
    public async Task WaitForBatchesAsync(int count)
    {
        for (var index = 0; index < count; index++)
            (await _started.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false)).Should().BeTrue();
    }
}
