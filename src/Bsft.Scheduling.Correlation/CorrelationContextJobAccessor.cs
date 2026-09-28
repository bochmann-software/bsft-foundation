using Bsft.Correlation;

namespace Bsft.Scheduling.Correlation;

/// <summary>
/// Connects <see cref="ICorrelationContext"/> to the job scheduler, so a job enqueued while handling
/// a request carries that request's correlation id into an execution that may happen hours later.
/// </summary>
/// <remarks>
/// The ambient id of an executing job wins over the scope's own id. Without that precedence a chain
/// of jobs — one job enqueuing the next — would start a new correlation on every hop, because each
/// run gets a fresh service scope whose <see cref="CorrelationContext"/> mints a new id.
/// </remarks>
internal sealed class CorrelationContextJobAccessor : IJobCorrelationAccessor
{
    private readonly ICorrelationContext _correlationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContextJobAccessor"/> class.
    /// </summary>
    /// <param name="correlationContext">The scoped correlation context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="correlationContext"/> is null.</exception>
    public CorrelationContextJobAccessor(ICorrelationContext correlationContext)
    {
        _correlationContext = correlationContext ?? throw new ArgumentNullException(nameof(correlationContext));
    }

    /// <inheritdoc />
    public string? CorrelationId => JobCorrelationScope.Current ?? _correlationContext.CorrelationId;
}
