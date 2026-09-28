using Bsft.Correlation;

namespace Bsft.EventAggregator.Saga.Correlation;

/// <summary>
/// Adopts the correlation id a step is running under into the step scope's
/// <see cref="CorrelationContext"/>, so that everything resolved in that scope — logging, tracing
/// and the audit trail — reports the id of whatever caused the step.
/// </summary>
internal sealed class SagaCorrelationInitializer : ISagaCorrelationInitializer
{
    private readonly CorrelationContext _correlationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaCorrelationInitializer"/> class.
    /// </summary>
    /// <param name="correlationContext">The step scope's own correlation context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="correlationContext"/> is null.</exception>
    public SagaCorrelationInitializer(CorrelationContext correlationContext)
    {
        _correlationContext = correlationContext ?? throw new ArgumentNullException(nameof(correlationContext));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="correlationId"/> is null, empty, or white space.
    /// </exception>
    public void Restore(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("The correlation id must not be empty.", nameof(correlationId));

        _correlationContext.CorrelationId = correlationId;
    }
}
