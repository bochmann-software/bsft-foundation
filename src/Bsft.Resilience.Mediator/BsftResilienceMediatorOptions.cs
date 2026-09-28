using System.Globalization;
using Bsft.Core;

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
    public Func<Exception?, bool> ShouldRetry { get; set; } = IsTransientFailure;

    /// <summary>
    /// The default retry predicate. Treats failures as transient, but never cancellation or the
    /// deterministic domain exceptions from <c>Bsft.Core</c> (validation, business rule, not found,
    /// forbidden, conflict), which will not succeed on a retry.
    /// </summary>
    /// <param name="exception">The exception thrown by the handler, if any.</param>
    /// <returns><see langword="true"/> if the exception is transient and should be retried; otherwise <see langword="false"/>.</returns>
    public static bool IsTransientFailure(Exception? exception) => exception switch
    {
        null => false,
        OperationCanceledException => false,
        ValidationException or BusinessRuleException or NotFoundException or ForbiddenException or ConflictException => false,
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
        if (MaxRetryAttempts < 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{nameof(BsftResilienceMediatorOptions)}.{nameof(MaxRetryAttempts)} must not be negative, but it is {MaxRetryAttempts}."));
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
