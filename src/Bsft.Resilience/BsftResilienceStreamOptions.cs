namespace Bsft.Resilience;

/// <summary>
/// Configuration for retrying a partially consumed <see cref="IAsyncEnumerable{T}"/> through
/// <see cref="ResilientStreamExtensions"/>: how often, how long to wait, which failures count, and what
/// happens when the stream broke after items were already delivered.
/// </summary>
/// <remarks>
/// A second root options type in this package rather than a group on <see cref="BsftResilienceOptions"/>,
/// for the reason that keeps the mediator options separate too: the HTTP options describe one named
/// client, while these describe one <c>WithRetry</c> call, and a process legitimately has streams with
/// very different budgets. It carries no <c>SectionName</c> and no <see cref="System.Net.Http.HttpClient"/>-style
/// configuration overload: half of it — <see cref="ShouldRetry"/> and <see cref="TimeProvider"/> — is
/// objects chosen in code, and the call it configures is a static method rather than a registration, so
/// there is no host-wide instance a section could bind. A host that keeps the numbers in
/// <c>appsettings.json</c> reads them itself and assigns them in the configure action.
/// </remarks>
public sealed class BsftResilienceStreamOptions
{
    /// <summary>The largest exponent the backoff doubles by, so a long budget cannot overflow the delay.</summary>
    private const int MaxBackoffExponent = 30;

    /// <summary>The smallest share of the computed delay that jitter may leave.</summary>
    private const double JitterFloor = 0.5;

    /// <summary>
    /// Gets or sets the maximum number of retries performed after the initial attempt. Defaults to 3.
    /// This is the budget for the <em>whole</em> stream: progress does not replenish it, so a stream that
    /// keeps breaking and restarting cannot retry forever.
    /// </summary>
    /// <remarks>
    /// Only the lower bound is enforced. There is no ceiling, for the same reason the HTTP and mediator
    /// options have none: how much a downstream dependency can be asked to bear is the host's call, not
    /// this package's. A very large budget is therefore accepted and is the caller's own load to own.
    /// </remarks>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay before the first retry. The effective delay doubles per retry and is
    /// capped by <see cref="MaxDelay"/>. Defaults to 200 milliseconds.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the ceiling for a single backoff delay. Defaults to 30 seconds. Must be at least
    /// <see cref="BaseDelay"/>.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the computed delay is randomized. When <see langword="true"/> (the default)
    /// the delay is scaled by a random factor of at least 0.5 and less than 1, so a fleet of consumers
    /// that broke on the same downstream failure does not retry in lockstep.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets the classifier deciding whether a thrown exception is worth another attempt.
    /// Defaults to <see cref="IsTransientFailure"/>.
    /// </summary>
    public Func<Exception, bool> ShouldRetry { get; set; } = IsTransientFailure;

    /// <summary>
    /// Gets or sets what happens when the stream fails after items were already yielded.
    /// Defaults to <see cref="Bsft.Resilience.PartialStreamPolicy.RethrowMidStream"/>, which never
    /// produces an item twice.
    /// </summary>
    public PartialStreamPolicy PartialStreamPolicy { get; set; } = PartialStreamPolicy.RethrowMidStream;

    /// <summary>
    /// Gets or sets the time provider the backoff waits on. Defaults to
    /// <see cref="System.TimeProvider.System"/>; a test substitutes a fake one so the waits are virtual.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// The default classifier. Treats every failure as transient except
    /// <see cref="OperationCanceledException"/>, which means the caller asked the enumeration to stop.
    /// </summary>
    /// <param name="exception">The exception the stream threw.</param>
    /// <returns><see langword="true"/> when another attempt is worth making; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// It is deliberately broader than the mediator's predicate, which also excludes the deterministic
    /// <c>Bsft.Core</c> domain exceptions. This package does not reference <c>Bsft.Core</c> and a stream
    /// of tokens from a remote model is not where those are thrown; a host that wants that reading passes
    /// <c>BsftResilienceMediatorOptions.IsTransientFailure</c> here.
    /// <para>
    /// Being broad, it also repeats failures that a second attempt cannot fix — a rejected credential, a
    /// malformed request, an argument the source refuses. Narrow it for a stream behind an authorization
    /// or validation boundary, where retrying only multiplies the load and delays the real error.
    /// </para>
    /// </remarks>
    public static bool IsTransientFailure(Exception exception) => exception is not OperationCanceledException;

    /// <summary>Computes the delay before the given retry, applying the cap and the jitter.</summary>
    /// <param name="retryNumber">The one-based number of the retry about to be made.</param>
    /// <returns>How long to wait.</returns>
    internal TimeSpan DelayBeforeRetry(int retryNumber)
    {
        if (BaseDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var exponent = Math.Min(retryNumber - 1, MaxBackoffExponent);
        var ticks = Math.Min(BaseDelay.Ticks * Math.Pow(2, exponent), MaxDelay.Ticks);

        if (UseJitter)
            ticks *= JitterFloor + (Random.Shared.NextDouble() * (1 - JitterFloor));

        return TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>Throws when a configured value cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    /// <remarks>
    /// Run on the <c>WithRetry</c> call rather than on the first failure, so a budget that could never
    /// work fails where the stack still names the call site instead of somewhere inside an enumeration
    /// that may not break for hours.
    /// </remarks>
    internal void Validate()
    {
        if (MaxRetryAttempts < 0)
            throw Invalid(nameof(MaxRetryAttempts), "must not be negative", MaxRetryAttempts);

        if (BaseDelay < TimeSpan.Zero)
            throw Invalid(nameof(BaseDelay), "must not be negative", BaseDelay);

        if (MaxDelay < BaseDelay)
        {
            throw Invalid(
                nameof(MaxDelay),
                FormattableString.Invariant($"must be at least {nameof(BaseDelay)} of {BaseDelay}"),
                MaxDelay);
        }

        if (!Enum.IsDefined(PartialStreamPolicy))
            throw Invalid(nameof(PartialStreamPolicy), "must name a declared policy", PartialStreamPolicy);

        if (ShouldRetry is null)
            throw Invalid(nameof(ShouldRetry), "must name a classifier", "null");

        if (TimeProvider is null)
            throw Invalid(nameof(TimeProvider), "must name a time provider", "null");
    }

    /// <summary>Names this type in the one exception shape every failure above takes.</summary>
    /// <param name="member">The member that cannot be used.</param>
    /// <param name="problem">What is wrong with it, as a clause completing "must …".</param>
    /// <param name="value">The offending value, named so the message is actionable.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException Invalid(string member, string problem, object value)
        => OptionsValidationFailure.For(nameof(BsftResilienceStreamOptions), member, problem, value);
}
