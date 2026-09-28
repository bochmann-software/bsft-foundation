namespace Bsft.Correlation;

/// <summary>
/// The constants a host needs in order to speak about correlation: the header name it travels under.
/// </summary>
public static class CorrelationDefaults
{
    /// <summary>
    /// The header that carries the correlation ID across HTTP boundaries: <c>X-Correlation-Id</c>.
    /// This is the single source of truth for the header name; the middleware, the delegating handler
    /// and every other component that reads or writes it reference this constant.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}
