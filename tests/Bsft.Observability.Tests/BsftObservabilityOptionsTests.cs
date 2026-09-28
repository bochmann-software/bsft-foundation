using Bsft.Observability.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Observability.Tests;

public sealed class BsftObservabilityOptionsTests
{
    [Fact]
    public void SectionName_IsTheDocumentedSection()
    {
        BsftObservabilityOptions.SectionName.Should().Be("Bsft:Observability");
    }

    [Fact]
    public void AddBsftObservability_BindsTheConfigurationSection()
    {
        var services = new ServiceCollection();
        services.AddBsftObservability(Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Observability:ServiceName"] = "orders-api",
            ["Bsft:Observability:ServiceVersion"] = "2.0.0",
            ["Bsft:Observability:EnableOtlpExporter"] = "false",
        }));

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<BsftObservabilityOptions>>().Value;

        options.ServiceName.Should().Be("orders-api");
        options.ServiceVersion.Should().Be("2.0.0");
        options.EnableOtlpExporter.Should().BeFalse();
    }

    [Fact]
    public void AddBsftObservability_EnableLogging_DefaultsToFalseAndCanBeBound()
    {
        var defaults = new ServiceCollection();
        defaults.AddBsftObservability(o => o.EnableOtlpExporter = false);
        using var defaultsProvider = defaults.BuildServiceProvider();
        defaultsProvider.GetRequiredService<IOptions<BsftObservabilityOptions>>().Value.EnableLogging.Should().BeFalse();

        var services = new ServiceCollection();
        services.AddBsftObservability(Configuration(new Dictionary<string, string?>
        {
            ["Bsft:Observability:EnableOtlpExporter"] = "false",
            ["Bsft:Observability:EnableLogging"] = "true",
        }));

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<BsftObservabilityOptions>>().Value.EnableLogging.Should().BeTrue();
    }

    [Fact]
    public void AddBsftObservability_ConfigureRunsAfterTheSection()
    {
        var services = new ServiceCollection();
        services.AddBsftObservability(
            Configuration(new Dictionary<string, string?> { ["Bsft:Observability:ServiceName"] = "bound" }),
            options =>
            {
                options.ServiceName = "overridden";
                options.EnableOtlpExporter = false;
            });

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IOptions<BsftObservabilityOptions>>().Value.ServiceName.Should().Be("overridden");
    }

    [Fact]
    public void AddBsftObservability_BlankServiceName_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftObservability(options => options.ServiceName = "  ");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ServiceName*");
    }

    [Fact]
    public void AddBsftObservability_BlankCorrelationHeaderName_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftObservability(options => options.CorrelationHeaderName = "");

        act.Should().Throw<InvalidOperationException>().WithMessage("*CorrelationHeaderName*");
    }

    [Fact]
    public void AddBsftObservability_NullServiceName_IsAccepted()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftObservability(options => options.EnableOtlpExporter = false);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBsftObservability_NeverRegistersTheOptionsInstanceItself()
    {
        var services = new ServiceCollection();
        services.AddBsftObservability(options => options.EnableOtlpExporter = false);

        services.Should().NotContain(sd => sd.ServiceType == typeof(BsftObservabilityOptions));
    }

    [Fact]
    public void Validation_ValidOptions_Succeeds()
    {
        var validation = new BsftObservabilityOptionsValidator();

        validation.Validate(name: null, new BsftObservabilityOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validation_BlankCorrelationHeaderName_FailsNamingTheMember()
    {
        var validation = new BsftObservabilityOptionsValidator();

        var result = validation.Validate(name: null, new BsftObservabilityOptions { CorrelationHeaderName = " " });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftObservabilityOptions.CorrelationHeaderName));
    }

    [Fact]
    public void Validation_NullOptions_ThrowsArgumentNullException()
    {
        var validation = new BsftObservabilityOptionsValidator();

        var act = () => validation.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void ValidateOnStart_UnusableOptions_FailsWhenTheOptionsAreRead()
    {
        // The registration call validates what it was given, so an unusable value can only reach the
        // start-time check by arriving from a source the call did not see.
        var services = new ServiceCollection();
        services.AddBsftObservability(options => options.EnableOtlpExporter = false);
        services.Configure<BsftObservabilityOptions>(options => options.ServiceName = "  ");

        var act = () => services.BuildServiceProvider().GetRequiredService<IOptions<BsftObservabilityOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ServiceName*");
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
