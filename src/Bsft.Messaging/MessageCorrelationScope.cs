namespace Bsft.Messaging;

/// <summary>
/// The default <see cref="IMessageCorrelationScope"/>, backed by an <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// The <see cref="AsyncLocal{T}"/> lives in an instance field, so the ambient value is shared by
/// exactly the components that share this instance. Registered as a singleton, that is the whole
/// container, which is what a bus restoring the id and the readers in a delivery's own service scope
/// need.
/// </remarks>
public sealed class MessageCorrelationScope : IMessageCorrelationScope
{
    private readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string? Current => _current.Value;

    /// <inheritdoc />
    public IDisposable Use(string? correlationId)
    {
        var previous = _current.Value;
        _current.Value = correlationId;
        return new Restorer(this, previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly MessageCorrelationScope _owner;
        private readonly string? _previous;

        public Restorer(MessageCorrelationScope owner, string? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose() => _owner._current.Value = _previous;
    }
}
