using Bsft.Resilience.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Tests;

public class ResilienceOptionsTests
{
    [Fact]
    public void SectionName_IsTheDocumentedSection()
    {
        BsftResilienceOptions.SectionName.Should().Be("Bsft:Resilience");
    }

    [Fact]
    public void AddBsftResilience_BindsTheConfigurationSection()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("bound").AddBsftResilience(Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Resilience:MaxRetryAttempts"] = "5",
        }));

        var act = () => services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("bound");

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBsftResilience_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("client");

        var act = () => builder.AddBsftResilience((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftResilientHttpClient_BindsTheConfigurationSection()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilientHttpClient("bound", Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Resilience:MaxRetryAttempts"] = "5",
        }));

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBsftResilientHttpClient_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilientHttpClient("client", (IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftResilientHttpClient_NullServicesWithConfiguration_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftResilientHttpClient("client", Configuration([]));

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Theory]
    [InlineData(nameof(BsftResilienceOptions.MaxRetryAttempts))]
    [InlineData(nameof(BsftResilienceOptions.BaseDelay))]
    [InlineData(nameof(BsftResilienceOptions.AttemptTimeout))]
    [InlineData(nameof(BsftResilienceOptions.TotalRequestTimeout))]
    [InlineData(nameof(BsftResilienceOptions.FailureRatio))]
    [InlineData(nameof(BsftResilienceOptions.SamplingDuration))]
    [InlineData(nameof(BsftResilienceOptions.MinimumThroughput))]
    [InlineData(nameof(BsftResilienceOptions.BreakDuration))]
    public void AddBsftResilience_UnusableValue_ThrowsNamingTheMember(string member)
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("client");

        var act = () => builder.AddBsftResilience(options => Break(options, member));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{member}*");
    }

    /// <summary>Sets exactly the one member the theory names to a value the pipeline cannot use.</summary>
    private static void Break(BsftResilienceOptions options, string member)
    {
        switch (member)
        {
            case nameof(BsftResilienceOptions.MaxRetryAttempts):
                options.MaxRetryAttempts = -1;
                break;
            case nameof(BsftResilienceOptions.BaseDelay):
                options.BaseDelay = TimeSpan.FromSeconds(-1);
                break;
            case nameof(BsftResilienceOptions.AttemptTimeout):
                options.AttemptTimeout = TimeSpan.Zero;
                break;
            case nameof(BsftResilienceOptions.TotalRequestTimeout):
                options.TotalRequestTimeout = options.AttemptTimeout;
                break;
            case nameof(BsftResilienceOptions.FailureRatio):
                options.FailureRatio = 0;
                break;
            case nameof(BsftResilienceOptions.SamplingDuration):
                options.SamplingDuration = options.AttemptTimeout;
                break;
            case nameof(BsftResilienceOptions.MinimumThroughput):
                options.MinimumThroughput = 1;
                break;
            default:
                options.BreakDuration = TimeSpan.Zero;
                break;
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
