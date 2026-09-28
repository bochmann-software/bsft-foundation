using Bsft.Correlation;

namespace Bsft.Testing.Contexts;

/// <summary>
/// A <see cref="CorrelationContext"/> with a fixed, readable correlation id, so assertions on logs,
/// outgoing headers, and audit rows can compare against a constant instead of a random GUID.
/// </summary>
/// <remarks>
/// It derives from <see cref="CorrelationContext"/> rather than only implementing
/// <see cref="Bsft.Correlation.ICorrelationContext"/> on purpose: the correlation middleware takes the concrete type,
/// so a double that is not one would be bypassed the moment the middleware runs.
/// </remarks>
public sealed class TestCorrelationContext : CorrelationContext
{
    /// <summary>The correlation id a default-constructed instance carries.</summary>
    public const string DefaultCorrelationId = "test-correlation-id";

    /// <summary>
    /// Initializes a new instance of the <see cref="TestCorrelationContext"/> class carrying
    /// <see cref="DefaultCorrelationId"/>.
    /// </summary>
    public TestCorrelationContext()
        : this(DefaultCorrelationId)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestCorrelationContext"/> class carrying the
    /// given correlation id.
    /// </summary>
    /// <param name="correlationId">The correlation id the context reports.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="correlationId"/> is null, empty, or white space.
    /// </exception>
    public TestCorrelationContext(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("The correlation id must not be empty.", nameof(correlationId));

        CorrelationId = correlationId;
    }
}
