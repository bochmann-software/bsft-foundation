namespace Bsft.Retention.Tests.Fakes;

/// <summary>A generic retention pass, so a test can assert the name a constructed generic pass gets.</summary>
/// <typeparam name="TArgument">Any type; it only shapes the name.</typeparam>
public sealed class GenericRetentionPass<TArgument> : IRetentionPass
{
    /// <inheritdoc />
    public Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
