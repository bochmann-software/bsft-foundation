using FluentAssertions;

namespace Bsft.Hosting.Tests;

public class BsftRateLimitingOptionsValidatorTests
{
    [Fact]
    public void Validate_WithDefaultOptions_Succeeds()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();

        var result = validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithValidPolicyContainingAllLimiterKinds_Succeeds()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        policy.Limiters.Add(new PolicyLimiterOptions { Kind = RateLimiterKind.FixedWindow });
        policy.Limiters.Add(new PolicyLimiterOptions { Kind = RateLimiterKind.SlidingWindow });
        policy.Limiters.Add(new PolicyLimiterOptions { Kind = RateLimiterKind.TokenBucket });
        policy.Limiters.Add(new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency });
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithPolicyHavingZeroLimiters_FailsNamingThePath()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        options.Policies["p"] = new RateLimitPolicyOptions();

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters must name at least one limiter.");
    }

    [Fact]
    public void Validate_WithPolicyValueNull_FailsNamingThePath()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        options.Policies["p"] = null!;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters must name at least one limiter.");
    }

    [Fact]
    public void Validate_WithPolicyNamedBsftFixedWindow_Fails()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        policy.Limiters.Add(new PolicyLimiterOptions());
        options.Policies[HostingDefaults.RateLimitingPolicyName] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("repeats the name of the default policy");
    }

    [Fact]
    public void Validate_WithPolicyNamedWhitespace_Fails()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        policy.Limiters.Add(new PolicyLimiterOptions());
        options.Policies["  "] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("a policy without a name");
    }

    [Fact]
    public void Validate_WithNullLimiterInList_FailsNamingThePath()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        policy.Limiters.Add(null!);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0 is empty.");
    }

    [Theory]
    [InlineData(0, "PermitLimit")]
    [InlineData(-1, "QueueLimit")]
    public void Validate_WithInvalidIntField_FailsNamingTheField(int invalidValue, string fieldName)
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions();

        if (fieldName == "PermitLimit")
            limiter.PermitLimit = invalidValue;
        else if (fieldName == "QueueLimit")
            limiter.QueueLimit = invalidValue;

        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain($"Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:{fieldName}");
    }

    [Fact]
    public void Validate_WithRetryAfterSecondsZero_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions { RetryAfterSeconds = 0 };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:RetryAfterSeconds must be at least one.");
    }

    [Fact]
    public void Validate_WithFixedWindowAndWindowSecondsZero_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            Kind = RateLimiterKind.FixedWindow,
            WindowSeconds = 0
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:WindowSeconds must be at least one.");
    }

    [Fact]
    public void Validate_WithSlidingWindowAndSegmentsPerWindowZero_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            Kind = RateLimiterKind.SlidingWindow,
            SegmentsPerWindow = 0
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:SegmentsPerWindow must be at least one.");
    }

    [Fact]
    public void Validate_WithTokenBucketAndTokensPerPeriodZero_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            Kind = RateLimiterKind.TokenBucket,
            TokensPerPeriod = 0
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:TokensPerPeriod must be at least one.");
    }

    [Fact]
    public void Validate_WithInvalidKind_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            Kind = (RateLimiterKind)99
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:Kind is not a known limiter kind.");
    }

    [Fact]
    public void Validate_WithInvalidPartitionBy_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PartitionBy = (RateLimitPartitionKind)99
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:PartitionBy is not a known partition kind.");
    }

    [Fact]
    public void Validate_WithPrincipalPartitionAndBlankClaimType_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PartitionBy = RateLimitPartitionKind.Principal,
            ClaimType = " "
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:ClaimType must name a claim");
    }

    [Fact]
    public void Validate_WithCustomPartitionAndMissingSelector_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PartitionBy = RateLimitPartitionKind.Custom,
            PartitionKeySelector = "missing"
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("'missing' is not registered");
    }

    [Fact]
    public void Validate_WithCustomPartitionAndNullSelector_FailsNamingTheField()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PartitionBy = RateLimitPartitionKind.Custom,
            PartitionKeySelector = null
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("is not registered");
    }

    [Fact]
    public void Validate_WithConcurrencyLimiterAndWindowSecondsZero_Succeeds()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            Kind = RateLimiterKind.Concurrency,
            WindowSeconds = 0
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithFixedWindowAndIgnoredSlidingWindowFields_Succeeds()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            Kind = RateLimiterKind.FixedWindow,
            SegmentsPerWindow = 0,
            TokensPerPeriod = 0
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithCustomPartitionAndRegisteredSelector_Succeeds()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        options.PartitionKeySelectors["session"] = _ => "x";
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PartitionBy = RateLimitPartitionKind.Custom,
            PartitionKeySelector = "session"
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithCustomPartitionAndRegisteredButNullSelector_Fails()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        options.PartitionKeySelectors["session"] = null!;
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PartitionBy = RateLimitPartitionKind.Custom,
            PartitionKeySelector = "session"
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("is not registered");
    }

    [Fact]
    public void Validate_WithMultipleInvalidFields_IncludesBothFailures()
    {
        var validator = new BsftRateLimitingOptionsValidator();
        var options = new BsftRateLimitingOptions();
        var policy = new RateLimitPolicyOptions();
        var limiter = new PolicyLimiterOptions
        {
            PermitLimit = 0,
            QueueLimit = -1
        };
        policy.Limiters.Add(limiter);
        options.Policies["p"] = policy;

        var result = validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCountGreaterThanOrEqualTo(2);
        result.FailureMessage.Should().Contain("PermitLimit");
        result.FailureMessage.Should().Contain("QueueLimit");
    }

    [Fact]
    public void Validate_WithNullOptions_Throws()
    {
        var validator = new BsftRateLimitingOptionsValidator();

        var act = () => validator.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void PolicyLimiterOptions_Defaults_AreCorrect()
    {
        var limiter = new PolicyLimiterOptions();

        limiter.Kind.Should().Be(RateLimiterKind.FixedWindow);
        limiter.PartitionBy.Should().Be(RateLimitPartitionKind.ClientAddress);
        limiter.ClaimType.Should().Be(System.Security.Claims.ClaimTypes.NameIdentifier);
        limiter.PartitionKeySelector.Should().BeNull();
        limiter.PermitLimit.Should().Be(100);
        limiter.WindowSeconds.Should().Be(60);
        limiter.SegmentsPerWindow.Should().Be(4);
        limiter.TokensPerPeriod.Should().Be(1);
        limiter.QueueLimit.Should().Be(0);
        limiter.RetryAfterSeconds.Should().BeNull();
    }
}
