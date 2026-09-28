namespace Bsft.Correlation;

/// <summary>
/// Well-known HTTP header names used to propagate a correlation ID across service boundaries.
/// </summary>
/// <remarks>
/// Superseded by <see cref="CorrelationDefaults"/>, which is where the conventions of this repository
/// put the constants a host needs. It is kept so that code naming it keeps compiling.
/// </remarks>
[Obsolete("Use CorrelationDefaults. This class will be removed in a future major version.")]
public static class CorrelationHeaders
{
    /// <summary>
    /// The header that carries the correlation ID across HTTP boundaries: <c>X-Correlation-Id</c>.
    /// </summary>
    public const string CorrelationId = CorrelationDefaults.CorrelationId;
}
