namespace Bsft.Resilience;

/// <summary>
/// Configuration for the BSFT HTTP resilience pipeline (retry, circuit breaker, timeout)
/// applied to outgoing <see cref="System.Net.Http.HttpClient"/> requests.
/// The defaults mirror the Polly standard resilience strategy.
/// </summary>
/// <remarks>
/// This is the package's primary options type and binds from <c>Bsft:Resilience</c>. It configures
/// one named client, so a host with two downstreams that deserve different retry budgets configures
/// each client's call separately; the section supplies the shared default that each call then
/// adjusts.
/// </remarks>
public sealed class BsftResilienceOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Resilience";

    /// <summary>The fewest actions Polly's circuit breaker accepts in a sampling window.</summary>
    private const int MinimumAllowedThroughput = 2;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts performed after the initial request.
    /// Defaults to 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retries. The effective delay grows exponentially
    /// and is randomized with jitter. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the timeout applied to a single attempt. Defaults to 10 seconds.
    /// Must be smaller than <see cref="TotalRequestTimeout"/>.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the total timeout spanning all attempts. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the failure ratio (between 0 and 1) within the sampling window that trips
    /// the circuit breaker. Defaults to 0.1.
    /// </summary>
    public double FailureRatio { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the sampling window the circuit breaker evaluates. Defaults to 30 seconds.
    /// Must be at least twice <see cref="AttemptTimeout"/>.
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the minimum number of actions that must occur within the sampling window
    /// before the circuit breaker can trip. Defaults to 100.
    /// </summary>
    public int MinimumThroughput { get; set; } = 100;

    /// <summary>
    /// Gets or sets how long the circuit stays open before transitioning to half-open.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Throws when a configured value cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    /// <remarks>
    /// These are the rules Polly's standard strategy enforces when the handler is built. Stating them
    /// here moves the failure to the registration call, where the stack still names the client being
    /// configured, instead of to the first request through it.
    /// </remarks>
    internal void Validate()
    {
        if (MaxRetryAttempts < 0)
            throw Invalid(nameof(MaxRetryAttempts), "must not be negative", MaxRetryAttempts);

        if (BaseDelay < TimeSpan.Zero)
            throw Invalid(nameof(BaseDelay), "must not be negative", BaseDelay);

        if (AttemptTimeout <= TimeSpan.Zero)
            throw Invalid(nameof(AttemptTimeout), "must be positive", AttemptTimeout);

        if (TotalRequestTimeout <= AttemptTimeout)
        {
            throw Invalid(
                nameof(TotalRequestTimeout),
                FormattableString.Invariant(
                    $"must exceed {nameof(AttemptTimeout)} of {AttemptTimeout}"),
                TotalRequestTimeout);
        }

        if (FailureRatio is <= 0 or > 1)
            throw Invalid(nameof(FailureRatio), "must be greater than 0 and at most 1", FailureRatio);

        if (SamplingDuration < AttemptTimeout + AttemptTimeout)
        {
            throw Invalid(
                nameof(SamplingDuration),
                FormattableString.Invariant(
                    $"must be at least twice {nameof(AttemptTimeout)} of {AttemptTimeout}"),
                SamplingDuration);
        }

        if (MinimumThroughput < MinimumAllowedThroughput)
        {
            throw Invalid(
                nameof(MinimumThroughput),
                FormattableString.Invariant($"must be at least {MinimumAllowedThroughput}"),
                MinimumThroughput);
        }

        if (BreakDuration <= TimeSpan.Zero)
            throw Invalid(nameof(BreakDuration), "must be positive", BreakDuration);
    }

    /// <summary>Names this type in the one exception shape every failure above takes.</summary>
    /// <param name="member">The member that cannot be used.</param>
    /// <param name="problem">What is wrong with it, as a clause completing "must …".</param>
    /// <param name="value">The offending value, named so the message is actionable.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException Invalid(string member, string problem, object value)
        => OptionsValidationFailure.For(nameof(BsftResilienceOptions), member, problem, value);
}
