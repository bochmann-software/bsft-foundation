using System.ComponentModel;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// The marker for retryable requests as it was declared before it moved to <see cref="Bsft.Mediator.RetryableAttribute"/>
/// in <c>Bsft.Mediator.Abstractions</c>. <see cref="RetryBehavior{TRequest, TResponse}"/> honours both.
/// </summary>
/// <remarks>
/// <para>
/// A type forward cannot carry the move: a forward keeps the full type name, and a type in
/// <c>Bsft.Mediator.Abstractions</c> is in the <c>Bsft.Mediator</c> namespace. The type therefore stays here,
/// so that an assembly compiled against an older <c>Bsft.Resilience.Mediator</c> still loads and its requests are
/// still retried.
/// </para>
/// <para>
/// New code marks requests with <see cref="Bsft.Mediator.RetryableAttribute"/>. A file importing both
/// <c>Bsft.Mediator</c> and <c>Bsft.Resilience.Mediator</c> sees two attributes named <c>Retryable</c> and has to
/// choose; dropping the import of <c>Bsft.Resilience.Mediator</c>, which such a file usually needs for nothing
/// else, picks the new one. Apply either only to idempotent requests.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
public sealed class RetryableAttribute : Attribute;
