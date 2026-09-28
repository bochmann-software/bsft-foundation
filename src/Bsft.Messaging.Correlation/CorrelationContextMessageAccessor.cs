using Bsft.Correlation;

namespace Bsft.Messaging.Correlation;

/// <summary>
/// Connects <see cref="ICorrelationContext"/> to messaging, so an integration event staged while
/// handling a request carries that request's correlation id into a delivery that may happen much later
/// and in another service.
/// </summary>
/// <remarks>
/// The ambient id of a message being handled wins over the scope's own id. Without that precedence a
/// chain of integration events — a handler that stages the next event — would start a new correlation
/// on every hop, because the delivery runs in a fresh service scope whose <see cref="CorrelationContext"/>
/// mints a new id.
/// </remarks>
internal sealed class CorrelationContextMessageAccessor : IMessageCorrelationAccessor
{
    private readonly ICorrelationContext _correlationContext;
    private readonly IMessageCorrelationScope _messageCorrelationScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContextMessageAccessor"/> class.
    /// </summary>
    /// <param name="correlationContext">The scoped correlation context.</param>
    /// <param name="messageCorrelationScope">The ambient correlation scope of a message being handled.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CorrelationContextMessageAccessor(
        ICorrelationContext correlationContext, IMessageCorrelationScope messageCorrelationScope)
    {
        _correlationContext = correlationContext ?? throw new ArgumentNullException(nameof(correlationContext));
        _messageCorrelationScope = messageCorrelationScope ?? throw new ArgumentNullException(nameof(messageCorrelationScope));
    }

    /// <inheritdoc />
    public string? CorrelationId => _messageCorrelationScope.Current ?? _correlationContext.CorrelationId;
}
