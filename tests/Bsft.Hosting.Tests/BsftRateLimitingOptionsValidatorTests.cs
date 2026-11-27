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

    private static string Fail(BsftRateLimitingOptions options)
    {
        var result = new BsftRateLimitingOptionsValidator().Validate(name: null, options);
        result.Failed.Should().BeTrue();
        return result.FailureMessage!;
    }

    private static BsftRateLimitingOptions WithLimiter(PolicyLimiterOptions limiter)
    {
        var options = new BsftRateLimitingOptions();
        options.Policies["p"] = new RateLimitPolicyOptions { Limiters = { limiter } };
        return options;
    }

    [Fact]
    public void Validate_WithRootWindowBelowOneSecond_Fails()
    {
        Fail(new BsftRateLimitingOptions { Window = TimeSpan.FromMilliseconds(500) })
            .Should().Contain("Bsft:Hosting:RateLimiting:Window must be at least one second.");
    }

    [Fact]
    public void Validate_WithRootWindowAndDisagreeingWindowSeconds_Fails()
    {
        Fail(new BsftRateLimitingOptions { Window = TimeSpan.FromSeconds(30), WindowSeconds = 90 })
            .Should().Contain("Bsft:Hosting:RateLimiting:Window (00:00:30) and WindowSeconds (90) disagree");
    }

    [Theory]
    [InlineData(60, 30)]
    [InlineData(30, 30)]
    public void Validate_WithRootWindowAndUnchangedOrAgreeingWindowSeconds_Succeeds(int seconds, int windowSeconds)
    {
        var options = new BsftRateLimitingOptions { Window = TimeSpan.FromSeconds(windowSeconds), WindowSeconds = seconds };

        new BsftRateLimitingOptionsValidator().Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithLimiterWindowBelowOneSecond_FailsNamingThePath()
    {
        var options = WithLimiter(new PolicyLimiterOptions { Window = TimeSpan.Zero });

        Fail(options).Should().Contain("Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:Window must be at least one second.");
    }

    [Fact]
    public void Validate_WithLimiterWindowAndDisagreeingWindowSeconds_Fails()
    {
        var options = WithLimiter(new PolicyLimiterOptions { Window = TimeSpan.FromSeconds(30), WindowSeconds = 90 });

        Fail(options).Should().Contain("Policies:p:Limiters:0:Window (00:00:30) and WindowSeconds (90) disagree");
    }

    [Fact]
    public void Validate_WithConcurrencyLimiterAndTinyWindow_Succeeds()
    {
        var options = WithLimiter(new PolicyLimiterOptions { Kind = RateLimiterKind.Concurrency, Window = TimeSpan.Zero });

        new BsftRateLimitingOptionsValidator().Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidGlobalLimiter_Succeeds()
    {
        var options = new BsftRateLimitingOptions
        {
            GlobalLimiter = new PolicyLimiterOptions { Window = TimeSpan.FromSeconds(30) }
        };

        new BsftRateLimitingOptionsValidator().Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithInvalidGlobalLimiter_FailsNamingThePath()
    {
        var options = new BsftRateLimitingOptions
        {
            GlobalLimiter = new PolicyLimiterOptions { PermitLimit = 0 }
        };

        Fail(options).Should().Contain("Bsft:Hosting:RateLimiting:GlobalLimiter:PermitLimit must be at least one.");
    }

    [Fact]
    public void Validate_WithGlobalLimiterNamingAnUnregisteredSelector_Fails()
    {
        var options = new BsftRateLimitingOptions
        {
            GlobalLimiter = new PolicyLimiterOptions
            {
                PartitionBy = RateLimitPartitionKind.Custom,
                PartitionKeySelector = "missing"
            }
        };

        Fail(options).Should().Contain("Bsft:Hosting:RateLimiting:GlobalLimiter:PartitionKeySelector");
    }

    [Fact]
    public void Validate_WithNullCustomPartitionKeyPrefix_Fails()
    {
        Fail(new BsftRateLimitingOptions { CustomPartitionKeyPrefix = null! })
            .Should().Contain("Bsft:Hosting:RateLimiting:CustomPartitionKeyPrefix must not be null.");
    }

    [Fact]
    public void Validate_WithEmptyCustomPartitionKeyPrefix_Succeeds()
    {
        new BsftRateLimitingOptionsValidator()
            .Validate(name: null, new BsftRateLimitingOptions { CustomPartitionKeyPrefix = "" })
            .Succeeded.Should().BeTrue();
    }
}
