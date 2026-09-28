namespace Bsft.Messaging;

/// <summary>
/// Default <see cref="IMessageCorrelationAccessor"/> that only knows the ambient id of a message
/// currently being handled. It is what a host without <c>Bsft.Messaging.Correlation</c> gets: an event
/// staged while handling another event keeps that event's correlation id, and one staged while
/// handling a request carries none.
/// </summary>
internal sealed class AmbientMessageCorrelationAccessor : IMessageCorrelationAccessor
{
    private readonly IMessageCorrelationScope _scope;

    /// <summary>
    /// Initializes a new instance of the <see cref="AmbientMessageCorrelationAccessor"/> class.
    /// </summary>
    /// <param name="scope">The ambient correlation scope of a message being handled.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope"/> is null.</exception>
    public AmbientMessageCorrelationAccessor(IMessageCorrelationScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <inheritdoc />
    public string? CorrelationId => _scope.Current;
}
