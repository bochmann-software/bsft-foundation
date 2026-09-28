namespace Bsft.Resilience.Mediator;

/// <summary>
/// Opt-in marker for mediator requests that may be safely retried on transient failures.
/// </summary>
/// <remarks>
/// Apply this only to idempotent requests. Retrying a non-idempotent command can produce
/// duplicate side effects (double charges, duplicate records). Requests without this attribute
/// pass through <see cref="RetryBehavior{TRequest, TResponse}"/> untouched.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
public sealed class RetryableAttribute : Attribute
{
}
