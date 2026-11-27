namespace Bsft.Mediator;

/// <summary>
/// Opt-in marker for mediator requests that may be safely retried on transient failures.
/// </summary>
/// <remarks>
/// <para>
/// Apply this only to idempotent requests. Retrying a non-idempotent command can produce duplicate side
/// effects (double charges, duplicate records). Requests without this attribute pass through the retry
/// behavior of <c>Bsft.Resilience.Mediator</c> untouched.
/// </para>
/// <para>
/// It lives in the contracts package, next to <see cref="TransactionalAttribute"/>, so that an assembly of
/// request contracts can declare a request retryable without referencing the resilience package, Polly and
/// the options that come with it. <c>Bsft.Resilience.Mediator.RetryableAttribute</c>, where the marker was
/// declared before, keeps working: the retry behavior honours either attribute.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
public sealed class RetryableAttribute : Attribute;
