using System.Globalization;
using Bsft.Core;
using Bsft.Core.Persistence;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Configuration for the mediator resilience pipeline that retries requests marked with
/// <see cref="RetryableAttribute"/> on transient failures.
/// </summary>
/// <remarks>
/// A root options type of its own rather than a nested <c>&lt;Concept&gt;Options</c> group on
/// <c>BsftResilienceOptions</c> in <c>Bsft.Resilience</c>, and it binds from its own section. The two
/// configure independent pipelines on different lifetimes: the HTTP options describe one named client, of which
/// an application legitimately has several with different retry budgets, while there is exactly one
/// mediator pipeline for the whole process. Folding the second into the first would offer a host one
/// object to configure once where the code has two, and would tie the mediator's retry budget to
/// whichever client happened to be registered last.
/// </remarks>
public sealed class BsftResilienceMediatorOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Resilience:Mediator";

    /// <summary>
    /// Gets or sets the maximum number of retry attempts performed after the initial invocation.
    /// Defaults to 3.
    /// </summary>
    /// <remarks>
    /// The value counts retries, not attempts: with the default a request runs at most four times.
    /// It is at least <c>1</c>; a request that is not to be retried is not marked
    /// <see cref="RetryableAttribute"/>.
    /// </remarks>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retries. The effective delay grows exponentially
    /// and is randomized with jitter. Defaults to 200 milliseconds.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the predicate deciding whether a thrown exception is transient and should
    /// be retried. Defaults to <see cref="IsTransientFailure"/>.
    /// </summary>
    /// <remarks>
    /// With the shared transaction of <c>Bsft.Data</c> registered, a failure the provider's transaction
    /// conflict classifier calls a conflict (PostgreSQL <c>40001</c>, <c>40P01</c>) is retried whatever
    /// this predicate says. That classifier is the intended source for database transients; this
    /// predicate is for everything else.
    /// </remarks>
    public Func<Exception?, bool> ShouldRetry { get; set; } = IsTransientFailure;

    /// <summary>
    /// Gets or sets a value indicating whether every command is retried as if it carried
    /// <see cref="RetryableAttribute"/>. Queries are still retried only where marked. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the outermost retried request runs again: a request dispatched from inside a retried one
    /// passes through, so its failure reaches the outer attempt instead of being retried on its own.
    /// Every handler of a retried command runs again, so work outside the database has to be idempotent
    /// or go through an outbox.
    /// </para>
    /// <para>
    /// A startup validator refuses this option unless every command is a transaction of its own
    /// (<c>ApplyTransactionToAllCommands()</c> on the mediator): a retried command that is not one would
    /// repeat the writes its failed attempt already made. A property rather than a fluent
    /// <c>ApplyRetryToAllCommands()</c> beside the mediator's, because these options bind from
    /// configuration.
    /// </para>
    /// </remarks>
    public bool ShouldRetryAllCommands { get; set; }

    /// <summary>
    /// The default retry predicate. Treats failures as transient, but never cancellation, the
    /// deterministic domain exceptions from <c>Bsft.Core</c> (validation, business rule, not found,
    /// forbidden, conflict) or a misuse of the ambient transaction
    /// (<see cref="AmbientTransactionMisuseException"/>), none of which succeeds on a retry.
    /// </summary>
    /// <param name="exception">The exception thrown by the handler, if any.</param>
    /// <returns><see langword="true"/> if the exception is transient and should be retried; otherwise <see langword="false"/>.</returns>
    public static bool IsTransientFailure(Exception? exception) => exception switch
    {
        null => false,
        OperationCanceledException => false,
        ValidationException or BusinessRuleException or NotFoundException or ForbiddenException or ConflictException => false,
        AmbientTransactionMisuseException => false,
        _ => true,
    };

    /// <summary>Throws when a configured value cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    /// <remarks>
    /// <see cref="ShouldRetry"/> is checked here as well as in the pipeline provider. A configuration
    /// section cannot null it — it has no binding — but a <c>Configure</c> delegate can, and the
    /// resulting failure is a null reference inside Polly on the first retried request rather than a
    /// message naming the member.
    /// </remarks>
    internal void Validate()
    {
        if (MaxRetryAttempts < 1)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{nameof(BsftResilienceMediatorOptions)}.{nameof(MaxRetryAttempts)} must be at least 1, but it is {MaxRetryAttempts}. A request that is not to be retried is not marked [Retryable]."));
        }

        if (BaseDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{nameof(BsftResilienceMediatorOptions)}.{nameof(BaseDelay)} must not be negative, but it is {BaseDelay}."));
        }

        if (ShouldRetry is null)
        {
            throw new InvalidOperationException(
                $"{nameof(BsftResilienceMediatorOptions)}.{nameof(ShouldRetry)} must name a predicate, but it is null.");
        }
    }
}
