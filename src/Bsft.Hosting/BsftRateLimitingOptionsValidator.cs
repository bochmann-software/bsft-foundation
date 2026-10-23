using Microsoft.Extensions.Options;

namespace Bsft.Hosting;

/// <summary>
/// Validates the named policies of <see cref="BsftRateLimitingOptions"/>, which data annotations cannot
/// reach: they sit in a dictionary and a list, and several rules depend on the limiter's kind.
/// </summary>
/// <remarks>
/// Each failure names the configuration path of the offending member, so a typo in
/// <c>appsettings.json</c> is reported where it was made, at start rather than on the first request.
/// </remarks>
internal sealed class BsftRateLimitingOptionsValidator : IValidateOptions<BsftRateLimitingOptions>
{
    /// <summary>Validates one configured instance.</summary>
    /// <param name="name">The named instance, unused: this package has one.</param>
    /// <param name="options">The instance to validate.</param>
    /// <returns>Success, or a failure listing every offending member.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftRateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        foreach (var (policyName, policy) in options.Policies)
            ValidatePolicy(options, policyName, policy, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Collects the failures of one named policy.</summary>
    /// <param name="options">The options the policy belongs to.</param>
    /// <param name="policyName">The policy's name.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="failures">Receives the failures.</param>
    private static void ValidatePolicy(
        BsftRateLimitingOptions options, string policyName, RateLimitPolicyOptions? policy, List<string> failures)
    {
        var path = $"{BsftRateLimitingOptions.SectionName}:Policies:{policyName}";

        if (string.IsNullOrWhiteSpace(policyName))
            failures.Add($"{BsftRateLimitingOptions.SectionName}:Policies has a policy without a name.");

        if (string.Equals(policyName, HostingDefaults.RateLimitingPolicyName, StringComparison.Ordinal))
            failures.Add($"{path} repeats the name of the default policy, which is registered already.");

        if (policy is null || policy.Limiters.Count == 0)
        {
            failures.Add($"{path}:Limiters must name at least one limiter.");
            return;
        }

        for (var index = 0; index < policy.Limiters.Count; index++)
            ValidateLimiter(options, $"{path}:Limiters:{index}", policy.Limiters[index], failures);
    }

    /// <summary>Collects the failures of one limiter of a policy.</summary>
    /// <param name="options">The options the limiter belongs to.</param>
    /// <param name="path">The limiter's configuration path.</param>
    /// <param name="limiter">The limiter.</param>
    /// <param name="failures">Receives the failures.</param>
    private static void ValidateLimiter(
        BsftRateLimitingOptions options, string path, PolicyLimiterOptions? limiter, List<string> failures)
    {
        if (limiter is null)
        {
            failures.Add($"{path} is empty.");
            return;
        }

        ValidateAlgorithm(path, limiter, failures);
        ValidatePartition(options, path, limiter, failures);
    }

    /// <summary>Collects the failures of a limiter's algorithm and its numbers.</summary>
    /// <param name="path">The limiter's configuration path.</param>
    /// <param name="limiter">The limiter.</param>
    /// <param name="failures">Receives the failures.</param>
    private static void ValidateAlgorithm(string path, PolicyLimiterOptions limiter, List<string> failures)
    {
        if (!Enum.IsDefined(limiter.Kind))
            failures.Add($"{path}:Kind is not a known limiter kind.");

        RequireAtLeastOne(failures, $"{path}:PermitLimit", limiter.PermitLimit, applies: true);
        RequireAtLeastOne(failures, $"{path}:WindowSeconds", limiter.WindowSeconds, limiter.Kind != RateLimiterKind.Concurrency);
        RequireAtLeastOne(failures, $"{path}:SegmentsPerWindow", limiter.SegmentsPerWindow, limiter.Kind == RateLimiterKind.SlidingWindow);
        RequireAtLeastOne(failures, $"{path}:TokensPerPeriod", limiter.TokensPerPeriod, limiter.Kind == RateLimiterKind.TokenBucket);
        RequireAtLeastOne(failures, $"{path}:RetryAfterSeconds", limiter.RetryAfterSeconds ?? 1, applies: true);

        if (limiter.QueueLimit < 0)
            failures.Add($"{path}:QueueLimit must not be negative.");
    }

    /// <summary>Collects the failures of a limiter's partition.</summary>
    /// <param name="options">The options holding the selectors.</param>
    /// <param name="path">The limiter's configuration path.</param>
    /// <param name="limiter">The limiter.</param>
    /// <param name="failures">Receives the failures.</param>
    private static void ValidatePartition(
        BsftRateLimitingOptions options, string path, PolicyLimiterOptions limiter, List<string> failures)
    {
        switch (limiter.PartitionBy)
        {
            case RateLimitPartitionKind.ClientAddress or RateLimitPartitionKind.Global:
                return;
            case RateLimitPartitionKind.Principal when string.IsNullOrWhiteSpace(limiter.ClaimType):
                failures.Add($"{path}:ClaimType must name a claim when the limiter partitions by principal.");
                return;
            case RateLimitPartitionKind.Principal:
                return;
            case RateLimitPartitionKind.Custom when !IsSelectorRegistered(options, limiter.PartitionKeySelector):
                failures.Add(
                    $"{path}:PartitionKeySelector must name a selector of BsftRateLimitingOptions.PartitionKeySelectors when the limiter partitions by a custom key; '{limiter.PartitionKeySelector}' is not registered.");
                return;
            case RateLimitPartitionKind.Custom:
                return;
            default:
                failures.Add($"{path}:PartitionBy is not a known partition kind.");
                return;
        }
    }

    /// <summary>Adds a failure when a member that applies to the limiter's kind is below one.</summary>
    /// <param name="failures">Receives the failure.</param>
    /// <param name="member">The member's configuration path.</param>
    /// <param name="value">Its value.</param>
    /// <param name="applies">Whether the limiter's kind reads the member at all.</param>
    private static void RequireAtLeastOne(List<string> failures, string member, int value, bool applies)
    {
        if (applies && value < 1)
            failures.Add($"{member} must be at least one.");
    }

    /// <summary>Whether <paramref name="selectorName"/> names a registered selector.</summary>
    /// <param name="options">The options holding the selectors.</param>
    /// <param name="selectorName">The name a limiter refers to.</param>
    /// <returns><see langword="true"/> when a selector is registered under that name.</returns>
    private static bool IsSelectorRegistered(BsftRateLimitingOptions options, string? selectorName)
        => selectorName is not null
            && options.PartitionKeySelectors.TryGetValue(selectorName, out var selector)
            && selector is not null;
}
