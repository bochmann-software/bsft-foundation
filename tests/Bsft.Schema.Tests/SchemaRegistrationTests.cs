using Bsft.Schema.DependencyInjection;
using Bsft.Schema.Reading;
using Bsft.Schema.Tests.Support;
using Bsft.Schema.ValueValidation;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Schema.Tests;

/// <summary>Registering the reader and the validator, and the bounds they are held to.</summary>
public sealed class SchemaRegistrationTests
{
    [Fact]
    public void AddBsftSchema_WithNothingElse_RegistersBothSeamsWithTheShippedBounds()
    {
        using var provider = new ServiceCollection().AddBsftSchema().BuildServiceProvider();

        provider.GetRequiredService<ISchemaReader>().Should().BeOfType<SchemaReader>();
        provider.GetRequiredService<ISchemaValidator>().Should().BeOfType<SchemaValidator>();

        var bounds = provider.GetRequiredService<IOptions<BsftSchemaOptions>>().Value;

        bounds.MaxDepth.Should().Be(8);
        bounds.MaxProperties.Should().Be(100);
        bounds.MaxSchemaNodes.Should().Be(500);
        bounds.MaxArrayItems.Should().Be(1000);
        bounds.PatternTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void AddBsftSchema_WithACallback_AppliesIt()
    {
        using var provider = new ServiceCollection()
            .AddBsftSchema(bounds => bounds.MaxDepth = 3)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftSchemaOptions>>().Value.MaxDepth.Should().Be(3);
    }

    [Fact]
    public void AddBsftSchema_WithASectionAndACallback_RunsTheCallbackAfterTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Bsft:Schema:MaxDepth"] = "4",
                ["Bsft:Schema:MaxArrayItems"] = "7",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddBsftSchema(configuration, bounds => bounds.MaxDepth = 5)
            .BuildServiceProvider();

        var bounds = provider.GetRequiredService<IOptions<BsftSchemaOptions>>().Value;

        bounds.MaxDepth.Should().Be(5);
        bounds.MaxArrayItems.Should().Be(7);
    }

    [Fact]
    public void AddBsftSchema_AfterAHostRegisteredItsOwn_KeepsTheHosts()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISchemaReader>(TestSchema.Reader());
        services.AddBsftSchema();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<ISchemaReader>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("MaxDepth")]
    [InlineData("MaxProperties")]
    [InlineData("MaxSchemaNodes")]
    [InlineData("MaxArrayItems")]
    public void AddBsftSchema_ABoundOfZero_FailsTheHostsStart(string member)
    {
        // A zero is not a permissive setting but an absent one, and every one of these is a ceiling on
        // an untrusted input.
        using var provider = new ServiceCollection()
            .AddBsftSchema(bounds => typeof(BsftSchemaOptions).GetProperty(member)!.SetValue(bounds, 0))
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftSchemaOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{member}*");
    }

    [Fact]
    public void AddBsftSchema_APatternTimeoutOfZero_FailsTheHostsStart()
    {
        using var provider = new ServiceCollection()
            .AddBsftSchema(bounds => bounds.PatternTimeout = TimeSpan.Zero)
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftSchemaOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*PatternTimeout*");
    }

    [Fact]
    public void AddBsftSchema_WithoutItsArguments_Refuses()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var noServices = () => SchemaServiceCollectionExtensions.AddBsftSchema(null!);
        var noServicesWithSection = () => SchemaServiceCollectionExtensions.AddBsftSchema(null!, configuration);
        var noConfiguration = () => services.AddBsftSchema((IConfiguration)null!);

        noServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        noServicesWithSection.Should().Throw<ArgumentNullException>().WithParameterName("services");
        noConfiguration.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Ctors_WithoutOptions_Refuse()
    {
        var noReaderOptions = () => new SchemaReader(null!);
        var noValidatorOptions = () => new SchemaValidator(null!);

        noReaderOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        noValidatorOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
