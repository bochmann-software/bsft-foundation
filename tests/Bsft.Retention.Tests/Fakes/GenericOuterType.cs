namespace Bsft.Retention.Tests.Fakes;

/// <summary>A generic type a pass is nested in, so a test can assert the name such a pass gets.</summary>
/// <typeparam name="TOuter">Any type; it only shapes the name.</typeparam>
public static class GenericOuterType<TOuter>
{
    /// <summary>A pass whose runtime name carries two arity markers.</summary>
    /// <typeparam name="TInner">Any type; it only shapes the name.</typeparam>
    public sealed class NestedRetentionPass<TInner> : IRetentionPass
    {
        /// <inheritdoc />
        public Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
