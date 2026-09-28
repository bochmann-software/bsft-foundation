namespace Bsft.Resilience.Tests.Fakes;

/// <summary>
/// Wraps a stream so that disposing its enumerator throws. It is the shape a broken network or database
/// cursor takes: the read fails and the teardown fails on top of it.
/// </summary>
internal sealed class FaultingOnDisposeStream : IAsyncEnumerable<int>
{
    private readonly IAsyncEnumerable<int> _inner;
    private readonly Exception _failure;

    /// <summary>Initializes a new instance of the <see cref="FaultingOnDisposeStream"/> class.</summary>
    /// <param name="inner">The stream to wrap.</param>
    /// <param name="failure">What disposal throws.</param>
    public FaultingOnDisposeStream(IAsyncEnumerable<int> inner, Exception failure)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _failure = failure ?? throw new ArgumentNullException(nameof(failure));
    }

    /// <inheritdoc />
    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(_inner.GetAsyncEnumerator(cancellationToken), _failure);

    /// <summary>Passes every pull through and fails the teardown.</summary>
    private sealed class Enumerator : IAsyncEnumerator<int>
    {
        private readonly IAsyncEnumerator<int> _inner;
        private readonly Exception _failure;

        /// <summary>Initializes a new instance of the <see cref="Enumerator"/> class.</summary>
        /// <param name="inner">The enumerator to wrap.</param>
        /// <param name="failure">What disposal throws.</param>
        public Enumerator(IAsyncEnumerator<int> inner, Exception failure)
        {
            _inner = inner;
            _failure = failure;
        }

        /// <inheritdoc />
        public int Current => _inner.Current;

        /// <inheritdoc />
        public ValueTask<bool> MoveNextAsync() => _inner.MoveNextAsync();

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            throw _failure;
        }
    }
}
