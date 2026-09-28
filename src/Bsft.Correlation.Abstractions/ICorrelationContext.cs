namespace Bsft.Correlation;

/// <summary>
/// Provides access to the correlation ID for the current request scope.
/// </summary>
/// <remarks>
/// The value an implementation exposes here reaches a response header and a logging scope, and its
/// usual origin is a header the client wrote. An implementation therefore owes its consumers a value
/// that is safe in both places: visible characters only, no carriage return or line feed, and a
/// length it has bounded. <c>Bsft.Correlation</c>'s middleware is what fills the shipped
/// implementation, and it adopts a client's value only when it matches <c>[A-Za-z0-9._-]{1,128}</c>
/// anchored at both ends, generating a fresh identifier otherwise. Whatever else writes this property
/// is expected to be at least as strict, because a consumer that logs the value has no way to tell a
/// validated one from a raw one.
/// </remarks>
public interface ICorrelationContext
{
    /// <summary>
    /// Gets the correlation ID for the current request. Never a raw, unvalidated client value.
    /// </summary>
    string CorrelationId { get; }
}
