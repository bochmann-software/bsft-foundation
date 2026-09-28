using Bsft.Idempotency.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Bsft.Idempotency.Tests.DependencyInjection;

public class IdempotencyConfigurationBinderTests
{
    [Fact]
    public void Bind_AConfiguredMethodList_ReplacesTheDefaultRatherThanAppendingToIt()
    {
        var options = new BsftIdempotencyOptions();

        IdempotencyConfigurationBinder.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Bsft:Idempotency:Methods:0"] = "POST",
                ["Bsft:Idempotency:Methods:1"] = "PATCH",
            }),
            options);

        options.Methods.Should().Equal("POST", "PATCH");
    }

    [Fact]
    public void Bind_AConfiguredHeaderAllowlist_ReplacesTheDefault()
    {
        var options = new BsftIdempotencyOptions();

        IdempotencyConfigurationBinder.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Bsft:Idempotency:CachedResponseHeaders:0"] = "Location",
            }),
            options);

        options.CachedResponseHeaders.Should().Equal("Location");
    }

    [Fact]
    public void Bind_AConfiguredStatusList_ReplacesTheDefault()
    {
        var options = new BsftIdempotencyOptions();

        IdempotencyConfigurationBinder.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Bsft:Idempotency:NonReplayableStatusCodes:0"] = "429",
            }),
            options);

        options.NonReplayableStatusCodes.Should().Equal(429);
    }

    [Fact]
    public void Bind_ASectionThatNamesNoList_LeavesEveryDefaultAlone()
    {
        var options = new BsftIdempotencyOptions();

        IdempotencyConfigurationBinder.Bind(
            Configuration(new Dictionary<string, string?> { ["Bsft:Idempotency:ShouldRequireKey"] = "true" }),
            options);

        options.ShouldRequireKey.Should().BeTrue();
        options.Methods.Should().Equal("POST");
        options.CachedResponseHeaders.Should().Equal("Location", "Content-Type");
        options.NonReplayableStatusCodes.Should().Equal(401, 403, 408, 429);
    }

    [Fact]
    public void Bind_TheScalarSettings_AreRead()
    {
        var options = new BsftIdempotencyOptions();

        IdempotencyConfigurationBinder.Bind(
            Configuration(new Dictionary<string, string?>
            {
                ["Bsft:Idempotency:HeaderName"] = "X-Key",
                ["Bsft:Idempotency:Retention"] = "01:00:00",
                ["Bsft:Idempotency:InProgressTimeout"] = "00:00:30",
                ["Bsft:Idempotency:MaxBodyBytes"] = "2048",
                ["Bsft:Idempotency:MaxKeyLength"] = "64",
                ["Bsft:Idempotency:Mode"] = "Enlisted",
            }),
            options);

        options.HeaderName.Should().Be("X-Key");
        options.Retention.Should().Be(TimeSpan.FromHours(1));
        options.InProgressTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.MaxBodyBytes.Should().Be(2048);
        options.MaxKeyLength.Should().Be(64);
        options.Mode.Should().Be(IdempotencyMode.Enlisted);
    }

    [Fact]
    public void Bind_NullConfiguration_IsRefused() =>
        FluentActions.Invoking(() => IdempotencyConfigurationBinder.Bind(null!, new BsftIdempotencyOptions()))
            .Should().Throw<ArgumentNullException>().WithParameterName("configuration");

    [Fact]
    public void Bind_NullOptions_IsRefused() =>
        FluentActions
            .Invoking(() => IdempotencyConfigurationBinder.Bind(
                Configuration(new Dictionary<string, string?>()), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
