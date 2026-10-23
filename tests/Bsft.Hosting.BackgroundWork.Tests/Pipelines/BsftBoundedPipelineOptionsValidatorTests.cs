using Bsft.Hosting.BackgroundWork.Pipelines;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class BsftBoundedPipelineOptionsValidatorTests
{
    private readonly BsftBoundedPipelineOptionsValidator _validation = ValidatorWithShutdown(TimeSpan.FromSeconds(30));

    [Fact]
    public void Validate_WhenTheOptionsAreTheDefaults_Succeeds()
    {
        _validation.Validate(name: null, new BsftBoundedPipelineOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenTheOptionsAreNull_Throws()
    {
        var act = () => _validation.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(nameof(BsftBoundedPipelineOptions.Capacity))]
    [InlineData(nameof(BsftBoundedPipelineOptions.MaxBatchSize))]
    [InlineData(nameof(BsftBoundedPipelineOptions.EnrichmentCapacity))]
    public void Validate_WhenACountIsNotPositive_NamesTheProperty(string property)
    {
        var options = new BsftBoundedPipelineOptions();
        typeof(BsftBoundedPipelineOptions).GetProperty(property)!.SetValue(options, 0);

        var result = _validation.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain(property);
    }

    [Theory]
    [InlineData(nameof(BsftBoundedPipelineOptions.FlushInterval), 0)]
    [InlineData(nameof(BsftBoundedPipelineOptions.FlushOnStopTimeout), -5)]
    [InlineData(nameof(BsftBoundedPipelineOptions.EnrichmentTimeout), 0)]
    [InlineData(nameof(BsftBoundedPipelineOptions.EnrichmentOnStopTimeout), 0)]
    [InlineData(nameof(BsftBoundedPipelineOptions.EnrichmentTimeout), int.MaxValue)]
    public void Validate_WhenAWaitIsNotPositiveOrTooLong_NamesTheProperty(string property, int seconds)
    {
        var options = new BsftBoundedPipelineOptions();
        var value = seconds == int.MaxValue ? TimeSpan.FromDays(60) : TimeSpan.FromSeconds(seconds);
        typeof(BsftBoundedPipelineOptions).GetProperty(property)!.SetValue(options, value);

        var result = _validation.Validate(name: null, options);

        result.Failures.Should().ContainSingle().Which.Should().Contain(property);
    }

    [Fact]
    public void Validate_WhenSeveralRulesFail_ReportsEveryFailure()
    {
        var options = new BsftBoundedPipelineOptions { Capacity = 0, MaxBatchSize = -1 };

        _validation.Validate(name: null, options).Failures.Should().HaveCount(2);
    }

    [Fact]
    public void NameFor_WhenAskedForAnItemType_IsItsFullName()
    {
        BsftBoundedPipelineOptions.NameFor<string>().Should().Be("System.String");
    }

    [Fact]
    public void Validate_WhenTheFlushBudgetExceedsTheHostsShutdownTimeout_Fails()
    {
        var validation = ValidatorWithShutdown(TimeSpan.FromSeconds(4));

        var result = validation.Validate(name: null, new BsftBoundedPipelineOptions { FlushOnStopTimeout = TimeSpan.FromSeconds(5) });

        result.Failures.Should().ContainSingle().Which.Should().Contain("ShutdownTimeout");
    }

    [Fact]
    public void Validate_WhenAStageIsRegisteredAndBothBudgetsExceedTheShutdownTimeout_Fails()
    {
        var validation = ValidatorWithShutdown(TimeSpan.FromSeconds(8));

        var options = new BsftBoundedPipelineOptions { HasEnrichmentStage = true };

        validation.Validate(name: null, options).Failures.Should().ContainSingle().Which.Should().Contain("ShutdownTimeout");
    }

    [Fact]
    public void Validate_WhenNoStageIsRegistered_OnlyTheFlushBudgetCounts()
    {
        var validation = ValidatorWithShutdown(TimeSpan.FromSeconds(8));

        validation.Validate(name: null, new BsftBoundedPipelineOptions()).Succeeded.Should().BeTrue();
    }

    private static BsftBoundedPipelineOptionsValidator ValidatorWithShutdown(TimeSpan shutdown) =>
        new(Options.Create(new Microsoft.Extensions.Hosting.HostOptions { ShutdownTimeout = shutdown }));

    [Fact]
    public void Constructor_WhenTheHostOptionsAreNull_Throws()
    {
        var act = () => new BsftBoundedPipelineOptionsValidator(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
