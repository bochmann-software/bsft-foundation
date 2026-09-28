using System.Text.Json;
using System.Text.Json.Serialization;
using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Tests;

public class JsonOptionsTests
{
    [Fact]
    public void BsftJsonOptions_Defaults_AreCorrect()
    {
        var options = new BsftJsonOptions();

        options.ShouldSerializeEnumsAsString.Should().BeTrue();
        options.ShouldOmitNullValues.Should().BeTrue();
        options.UsesCaseInsensitiveDeserialization.Should().BeTrue();
        options.DefaultDateFormat.Should().BeNull();
    }

    [Fact]
    public void AddBsftJsonDefaults_ConfiguresCamelCase()
    {
        var services = new ServiceCollection();
        services.AddBsftJsonDefaults();

        var provider = services.BuildServiceProvider();
        var mvcJsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>();

        mvcJsonOptions.Value.JsonSerializerOptions.PropertyNamingPolicy
            .Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void AddBsftJsonDefaults_ConfiguresEnumAsString()
    {
        var services = new ServiceCollection();
        services.AddBsftJsonDefaults();

        var provider = services.BuildServiceProvider();
        var mvcJsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>();

        mvcJsonOptions.Value.JsonSerializerOptions.Converters
            .Should().ContainItemsAssignableTo<JsonStringEnumConverter>();
    }

    [Fact]
    public void AddBsftJsonDefaults_ConfiguresNullHandling()
    {
        var services = new ServiceCollection();
        services.AddBsftJsonDefaults();

        var provider = services.BuildServiceProvider();
        var mvcJsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>();

        mvcJsonOptions.Value.JsonSerializerOptions.DefaultIgnoreCondition
            .Should().Be(JsonIgnoreCondition.WhenWritingNull);
    }

    [Fact]
    public void AddBsftJsonDefaults_ConfiguresCaseInsensitiveDeserialization()
    {
        var services = new ServiceCollection();
        services.AddBsftJsonDefaults();

        var provider = services.BuildServiceProvider();
        var mvcJsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>();

        mvcJsonOptions.Value.JsonSerializerOptions.PropertyNameCaseInsensitive
            .Should().BeTrue();
    }

    [Fact]
    public void AddBsftJsonDefaults_WithEnumAsStringDisabled_DoesNotAddConverter()
    {
        var services = new ServiceCollection();
        services.AddBsftJsonDefaults(o => o.ShouldSerializeEnumsAsString = false);

        var provider = services.BuildServiceProvider();
        var mvcJsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>();

        mvcJsonOptions.Value.JsonSerializerOptions.Converters
            .Should().NotContainItemsAssignableTo<JsonStringEnumConverter>();
    }

    [Fact]
    public void AddBsftJsonDefaults_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftJsonDefaults();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftJsonDefaults_ConfiguresHttpJsonOptions()
    {
        // Exercises JsonServiceCollectionExtensions.AddBsftJsonDefaults lines 29-31:
        // the ConfigureHttpJsonOptions lambda is triggered when IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> is resolved.
        var services = new ServiceCollection();
        services.AddBsftJsonDefaults();

        var provider = services.BuildServiceProvider();
        var httpJsonOptions = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();

        // Accessing .Value triggers the ConfigureHttpJsonOptions lambda (lines 29-31)
        httpJsonOptions.Value.SerializerOptions.PropertyNamingPolicy
            .Should().Be(JsonNamingPolicy.CamelCase);
    }


    [Fact]
    public void AddBsftJsonDefaults_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Json:ShouldOmitNullValues"] = "false",
                ["Bsft:Hosting:Json:ShouldSerializeEnumsAsString"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftJsonDefaults(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftJsonOptions>>().Value;

        options.ShouldOmitNullValues.Should().BeFalse();
        options.ShouldSerializeEnumsAsString.Should().BeFalse();

        var mvcJson = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value;
        mvcJson.JsonSerializerOptions.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.Never);
    }

    [Fact]
    public void AddBsftJsonDefaults_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftJsonDefaults(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftJsonDefaults_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftJsonDefaults(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
