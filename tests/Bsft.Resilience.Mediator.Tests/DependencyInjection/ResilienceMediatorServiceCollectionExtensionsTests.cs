using Bsft.Mediator;
using Bsft.Resilience.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests.DependencyInjection;

public class ResilienceMediatorServiceCollectionExtensionsTests
{
    [Retryable]
    private sealed record RetryableRequest;

    [Fact]
    public void AddBsftResilienceMediator_NullServices_Throws()
    {
        var act = () => ResilienceMediatorServiceCollectionExtensions.AddBsftResilienceMediator(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftResilienceMediator_RegistersOptionsProviderAndBehavior()
    {
        var services = new ServiceCollection();

        services.AddBsftResilienceMediator(o => o.MaxRetryAttempts = 5);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftResilienceMediatorOptions>>().Value.MaxRetryAttempts.Should().Be(5);

        var first = provider.GetRequiredService<IMediatorResiliencePipelineProvider>();
        var second = provider.GetRequiredService<IMediatorResiliencePipelineProvider>();
        first.Should().BeSameAs(second);
        first.Pipeline.Should().NotBeNull();

        provider.GetServices<IPipelineBehavior<RetryableRequest, string>>()
            .Should().ContainSingle(b => b is RetryBehavior<RetryableRequest, string>);
    }

    [Fact]
    public void AddBsftResilienceMediator_CalledTwice_RegistersASingleBehavior()
    {
        var services = new ServiceCollection();

        services.AddBsftResilienceMediator();
        services.AddBsftResilienceMediator();

        using var provider = services.BuildServiceProvider();

        // A second call is what a host and a shared extension each calling it looks like, and a
        // duplicated behavior would retry twice over and be flagged by the startup validator.
        provider.GetServices<IPipelineBehavior<RetryableRequest, string>>()
            .Should().ContainSingle(b => b is RetryBehavior<RetryableRequest, string>);
    }

    [Fact]
    public void AddBsftResilienceMediator_WithAConfiguration_BindsTheSection()
    {
        var services = new ServiceCollection();
        services.AddBsftResilienceMediator(Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Resilience:Mediator:MaxRetryAttempts"] = "7",
            ["Bsft:Resilience:Mediator:BaseDelay"] = "00:00:00.050",
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftResilienceMediatorOptions>>().Value;

        options.MaxRetryAttempts.Should().Be(7);
        options.BaseDelay.Should().Be(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void AddBsftResilienceMediator_WithBothAConfigurationAndAnAction_LetsTheActionWin()
    {
        var services = new ServiceCollection();
        services.AddBsftResilienceMediator(
            Configuration(new Dictionary<string, string?> { ["Bsft:Resilience:Mediator:MaxRetryAttempts"] = "7" }),
            options => options.MaxRetryAttempts = 2);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftResilienceMediatorOptions>>().Value
            .MaxRetryAttempts.Should().Be(2);
    }

    [Fact]
    public void AddBsftResilienceMediator_WithoutAConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilienceMediator((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftResilienceMediator_WithoutServicesButAConfiguration_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftResilienceMediator(Configuration([]));

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftResilienceMediator_WhateverItRegisters_NeverRegistersTheOptionsInstanceItself()
    {
        var services = new ServiceCollection();
        services.AddBsftResilienceMediator();

        services.Should().NotContain(sd => sd.ServiceType == typeof(BsftResilienceMediatorOptions));
    }

    [Theory]
    [InlineData(-1, nameof(BsftResilienceMediatorOptions.MaxRetryAttempts))]
    public void AddBsftResilienceMediator_WithAnUnusableRetryCount_Throws(int attempts, string member)
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilienceMediator(options => options.MaxRetryAttempts = attempts);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{member}*");
    }

    [Fact]
    public void AddBsftResilienceMediator_WithANegativeDelay_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilienceMediator(options => options.BaseDelay = TimeSpan.FromSeconds(-1));

        act.Should().Throw<InvalidOperationException>().WithMessage("*BaseDelay*");
    }

    [Fact]
    public void AddBsftResilienceMediator_WithoutAPredicate_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilienceMediator(options => options.ShouldRetry = null!);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ShouldRetry*");
    }

    [Fact]
    public void AddBsftResilienceMediator_WhenTheOptionsAreSpoiledAfterwards_FailsWhenTheyAreRead()
    {
        var services = new ServiceCollection();
        services.AddBsftResilienceMediator();
        services.Configure<BsftResilienceMediatorOptions>(options => options.MaxRetryAttempts = -1);

        var act = () => services.BuildServiceProvider()
            .GetRequiredService<IOptions<BsftResilienceMediatorOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxRetryAttempts*");
    }

    /// <summary>Builds a configuration holding the given keys.</summary>
    /// <param name="values">The keys and their values.</param>
    /// <returns>The configuration.</returns>
    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
