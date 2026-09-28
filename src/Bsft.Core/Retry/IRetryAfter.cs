namespace Bsft.Core.Retry;

/// <summary>
/// Implemented by an exception that knows when the work it failed should be attempted again. It is how
/// a component that has been told to wait — a service answering <c>429</c> with a <c>Retry-After</c>, a
/// vendor API asking for a pause, a lock that is held until a known instant — passes that instruction
/// on to whatever owns the retry, instead of sleeping where it stands.
/// </summary>
/// <remarks>
/// <para>
/// Sleeping where it stands is the failure mode this exists to remove. The component that learns of the
/// wait is usually the innermost one — a notification channel, an HTTP client — and the thing it would
/// block is a pipeline shared with every other piece of work, so one throttled recipient becomes a
/// stalled queue. Throwing instead makes the wait a property of the failed item, which the retrying
/// component can record and act on without holding anything.
/// </para>
/// <para>
/// The contract is deliberately an interface rather than a base class: the exception types that carry
/// this already exist and already have a hierarchy of their own, so a channel's
/// <c>ThrottledException</c> stays where it is in its own family and gains one property.
/// <see cref="RetryAfterException"/> is the ready-made implementation for code that has no such type.
/// </para>
/// <para>
/// It is a request, not an instruction. What honours it decides what it may cost: the messaging outbox
/// treats it as a floor and clamps it to a configured ceiling, so a service asking to be left alone for
/// a day does not remove a message from the pipeline for a day.
/// </para>
/// </remarks>
public interface IRetryAfter
{
    /// <summary>
    /// Gets how long to wait before the work is attempted again, or <see langword="null"/> when the
    /// failure asks for a later attempt without naming a wait.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is a real answer rather than a missing one: a <c>429</c> without a
    /// <c>Retry-After</c> header says "not now" and says nothing about when, and the component honouring
    /// this then applies whatever schedule it would have applied anyway. A non-positive value means the
    /// same thing.
    /// </remarks>
    TimeSpan? RetryAfter { get; }
}
