using System.Collections;

namespace Bsft.Logging.Tests.Fakes;

/// <summary>
/// A scope state that is an <see cref="IEnumerable{T}"/> of pairs and not an
/// <see cref="IReadOnlyList{T}"/>, so <c>BeginScope</c> takes its second branch.
/// </summary>
internal sealed class CustomKvpEnumerable : IEnumerable<KeyValuePair<string, object>>
{
    private readonly IEnumerable<KeyValuePair<string, object>> _inner;

    /// <summary>Initializes a new instance of the <see cref="CustomKvpEnumerable"/> class.</summary>
    /// <param name="inner">The pairs the scope carries.</param>
    public CustomKvpEnumerable(IEnumerable<KeyValuePair<string, object>> inner)
        => _inner = inner;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _inner.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
