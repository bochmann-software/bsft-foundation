using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Retention.Tests;

public sealed class BsftRetentionOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(Action<BsftRetentionOptions> configure, string? name = "TestPass")
    {
        var options = new BsftRetentionOptions();
        configure(options);

        return new BsftRetentionOptionsValidator().Validate(name, options);
    }

    [Fact]
    public void Validate_Defaults_Succeed()
    {
        // The defaults are "off but valid": no retention period, so nothing sweeps.
        var result = Validate(_ => { });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_FullyConfigured_Succeeds()
    {
        var result = Validate(o =>
        {
            o.RetentionPeriod = TimeSpan.FromDays(30);
            o.BatchSize = 250;
            o.BatchDelay = TimeSpan.FromSeconds(1);
            o.SweepInterval = TimeSpan.FromMinutes(30);
            o.MaxStartupStagger = TimeSpan.FromMinutes(5);
            o.MaxBatchesPerSweep = 50;
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroRetentionPeriod_Fails()
    {
        var result = Validate(o => o.RetentionPeriod = TimeSpan.Zero);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*RetentionPeriod*positive*");
    }

    [Fact]
    public void Validate_NegativeRetentionPeriod_Fails()
    {
        var result = Validate(o => o.RetentionPeriod = TimeSpan.FromDays(-1));

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*RetentionPeriod*positive*");
    }

    [Fact]
    public void Validate_NullRetentionPeriod_IsAccepted()
    {
        // Null is how a pass is turned off, not a misconfiguration.
        var result = Validate(o => o.RetentionPeriod = null);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_BatchSizeBelowOne_Fails()
    {
        var result = Validate(o => o.BatchSize = 0);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*BatchSize*at least 1*");
    }

    [Fact]
    public void Validate_MaxBatchesPerSweepBelowOne_Fails()
    {
        var result = Validate(o => o.MaxBatchesPerSweep = 0);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*MaxBatchesPerSweep*at least 1*");
    }

    [Fact]
    public void Validate_NegativeBatchDelay_Fails()
    {
        var result = Validate(o => o.BatchDelay = TimeSpan.FromSeconds(-1));

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*BatchDelay*not be negative*");
    }

    [Fact]
    public void Validate_ZeroBatchDelay_IsAccepted()
    {
        var result = Validate(o => o.BatchDelay = TimeSpan.Zero);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_NonPositiveSweepInterval_Fails()
    {
        var result = Validate(o => o.SweepInterval = TimeSpan.Zero);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*SweepInterval*positive*");
    }

    [Fact]
    public void Validate_NegativeMaxStartupStagger_Fails()
    {
        var result = Validate(o => o.MaxStartupStagger = TimeSpan.FromMinutes(-1));

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*MaxStartupStagger*not be negative*");
    }

    [Fact]
    public void Validate_ZeroMaxStartupStagger_IsAccepted()
    {
        var result = Validate(o => o.MaxStartupStagger = TimeSpan.Zero);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_SeveralBadValues_AreAllReportedAtOnce()
    {
        // One restart tells a host everything that is wrong with its section, not the first thing.
        var result = Validate(o =>
        {
            o.BatchSize = 0;
            o.SweepInterval = TimeSpan.Zero;
            o.BatchDelay = TimeSpan.FromSeconds(-1);
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }

    [Fact]
    public void Validate_FailureNamesThePass_SoAHostKnowsWhichOneIsWrong()
    {
        var result = Validate(o => o.BatchSize = 0, "Contoso.AuditLogRetentionPass");

        result.Failures.Should().ContainMatch("Contoso.AuditLogRetentionPass:*");
    }

    [Fact]
    public void Validate_NoName_FallsBackToTheOptionsTypeName()
    {
        var result = Validate(o => o.BatchSize = 0, name: null);

        result.Failures.Should().ContainMatch($"{nameof(BsftRetentionOptions)}:*");
    }

    [Fact]
    public void Validate_EmptyName_FallsBackToTheOptionsTypeName()
    {
        var result = Validate(o => o.BatchSize = 0, string.Empty);

        result.Failures.Should().ContainMatch($"{nameof(BsftRetentionOptions)}:*");
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var act = () => new BsftRetentionOptionsValidator().Validate("TestPass", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
