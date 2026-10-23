using Bsft.Correlation;

namespace Bsft.Logging;

/// <summary>
/// Tracks the scope values the file logger persists and reads them back for the line being written.
/// </summary>
/// <remarks>
/// The values in force are a chain of nodes, innermost first, held in an <see cref="AsyncLocal{T}"/> that all
/// instances share. A scope pushes one node per persisted key it carries and disposing it puts the previous head
/// back, so a nested scope shadows its parent per key and a sibling flow never sees it. The chain is walked, not
/// copied, so beginning a scope allocates one node per persisted value and a line allocates nothing for it.
/// </remarks>
internal sealed class LogScopeChain
{
    private static readonly AsyncLocal<ScopeNode?> Current = new();

    private readonly string[] _persistedKeys;
    private readonly string[] _extraKeys;
    private readonly bool _persistsCorrelationId;
    private readonly int _maxPropertyChars;

    /// <summary>Initializes a new instance of the <see cref="LogScopeChain"/> class.</summary>
    /// <param name="options">The logging options naming the persisted keys and the property budget.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public LogScopeChain(BsftLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _maxPropertyChars = options.MaxScopePropertyChars;

        // Distinct and in configured order. A key is cut to one line so it cannot forge a line either.
        _persistedKeys = options.PersistedScopeKeys
            .Select(key => key.ReplaceLineEndings(" "))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _persistsCorrelationId = _persistedKeys.Contains(LogScopeKeys.CorrelationId, StringComparer.Ordinal);
        _extraKeys = _persistedKeys
            .Where(key => !string.Equals(key, LogScopeKeys.CorrelationId, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>States the persisted values of a scope for everything logged until it is disposed.</summary>
    /// <typeparam name="TState">The scope state as the logging framework passed it.</typeparam>
    /// <param name="state">The scope state.</param>
    /// <returns>The scope, which puts the previous values back, or null when it carries no persisted key.</returns>
    public IDisposable? Begin<TState>(TState state)
        where TState : notnull
    {
        var previous = Current.Value;
        var head = previous;

        // Indexed for a list, which is the shape the logging framework passes, so the common case
        // walks the scope's values without allocating an enumerator for them.
        if (state is IReadOnlyList<KeyValuePair<string, object>> pairs)
        {
            for (var index = 0; index < pairs.Count; index++)
                head = Push(pairs[index], head);
        }
        else if (state is IEnumerable<KeyValuePair<string, object>> enumerable)
        {
            foreach (var pair in enumerable)
                head = Push(pair, head);
        }

        if (ReferenceEquals(head, previous))
            return null;

        Current.Value = head;
        return new Restorer(previous);
    }

    /// <summary>Reads the correlation id in force.</summary>
    /// <param name="noCorrelationId">What stands in for it outside any scope.</param>
    /// <returns>The id, or <paramref name="noCorrelationId"/>.</returns>
    public string CorrelationId(string noCorrelationId)
        => _persistsCorrelationId && TryFind(LogScopeKeys.CorrelationId, out var value)
            ? value ?? noCorrelationId
            : noCorrelationId;

    /// <summary>
    /// Collects the persisted values other than the correlation id in configured order, within the line's budget.
    /// </summary>
    /// <remarks>
    /// Values lose their line breaks. Keys and values count against the budget. A property that does not fit whole
    /// is dropped and never written cut, so a filter can miss such a line but never match a shortened value; later,
    /// smaller properties may still fit. A line that lost one gets the reserved truncation marker property, outside the budget.
    /// </remarks>
    /// <param name="properties">Receives the properties.</param>
    public void Collect(List<KeyValuePair<string, string>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var remaining = _maxPropertyChars;
        var dropped = false;
        foreach (var key in _extraKeys)
        {
            if (!TryFind(key, out var raw))
                continue;

            var value = (raw ?? string.Empty).ReplaceLineEndings(" ");
            if (key.Length + value.Length > remaining)
            {
                dropped = true;
                continue;
            }

            properties.Add(new KeyValuePair<string, string>(key, value));
            remaining -= key.Length + value.Length;
        }

        if (dropped)
            properties.Add(new KeyValuePair<string, string>(ScopePropertyEncoding.TruncatedKey, ScopePropertyEncoding.TruncatedValue));
    }

    private static bool TryFind(string key, out string? value)
    {
        for (var node = Current.Value; node is not null; node = node.Parent)
        {
            if (string.Equals(node.Key, key, StringComparison.Ordinal))
            {
                value = node.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private ScopeNode? Push(KeyValuePair<string, object> pair, ScopeNode? head)
    {
        foreach (var key in _persistedKeys)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                return new ScopeNode(key, pair.Value?.ToString(), head);
        }

        return head;
    }

    private sealed class ScopeNode
    {
        public ScopeNode(string key, string? value, ScopeNode? parent)
        {
            Key = key;
            Value = value;
            Parent = parent;
        }

        public string Key { get; }

        public string? Value { get; }

        public ScopeNode? Parent { get; }
    }

    private sealed class Restorer : IDisposable
    {
        private readonly ScopeNode? _previous;
        private bool _disposed;

        public Restorer(ScopeNode? previous)
        {
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // A second dispose must not put the values of a scope opened since back to an older state.
            if (_disposed)
                return;

            _disposed = true;
            Current.Value = _previous;
        }
    }
}
