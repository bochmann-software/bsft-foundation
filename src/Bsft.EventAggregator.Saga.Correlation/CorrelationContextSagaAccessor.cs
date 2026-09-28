using Bsft.Correlation;
using Bsft.EventAggregator.Saga.Timeouts;

namespace Bsft.EventAggregator.Saga.Correlation;

/// <summary>
/// Connects <see cref="ICorrelationContext"/> to the saga timeout machinery.
/// </summary>
/// <remarks>
/// The ambient id of a firing timeout wins over the scope's own id. Without that precedence a chain
/// of timeouts — a reminder that schedules the next reminder — would start a new correlation on every
/// hop, because the step runs in a fresh service scope whose <see cref="CorrelationContext"/> mints a
/// new id.
/// </remarks>
internal sealed class CorrelationContextSagaAccessor : ISagaCorrelationAccessor
{
    private readonly ICorrelationContext _correlationContext;
    private readonly ISagaCorrelationScope _sagaCorrelationScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContextSagaAccessor"/> class.
    /// </summary>
    /// <param name="correlationContext">The scoped correlation context.</param>
    /// <param name="sagaCorrelationScope">The ambient correlation scope of a firing timeout.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CorrelationContextSagaAccessor(ICorrelationContext correlationContext, ISagaCorrelationScope sagaCorrelationScope)
    {
        _correlationContext = correlationContext ?? throw new ArgumentNullException(nameof(correlationContext));
        _sagaCorrelationScope = sagaCorrelationScope ?? throw new ArgumentNullException(nameof(sagaCorrelationScope));
    }

    /// <inheritdoc />
    public string? CorrelationId => _sagaCorrelationScope.Current ?? _correlationContext.CorrelationId;
}
