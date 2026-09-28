namespace Bsft.Correlation;

/// <summary>
/// Scoped implementation of <see cref="ICorrelationContext"/> that stores the correlation ID for the current request.
/// </summary>
/// <remarks>
/// Deliberately left open for inheritance: a test host substitutes a fixed correlation ID by deriving
/// from this type (see <c>Bsft.Testing</c>'s <c>TestCorrelationContext</c>), which is only possible
/// because the middleware resolves the concrete type rather than the interface when it writes.
/// </remarks>
public class CorrelationContext : ICorrelationContext
{
    /// <inheritdoc />
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}
