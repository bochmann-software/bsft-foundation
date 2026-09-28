using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy.Sandboxes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy.Sandboxes;

/// <summary>Registering the sandbox seam, and the registry the host has to bring.</summary>
public sealed class SandboxTenantRegistrationTests
{
    [Fact]
    public void AddBsftSandboxTenants_WithTheHostsRegistry_ResolvesTheShippedProvisioner()
    {
        var services = new ServiceCollection();

        services.AddBsftSandboxTenants();
        services.AddSingleton(Substitute.For<ISandboxTenantRegistry>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISandboxTenantProvisioner>()
            .Should().BeOfType<SandboxTenantProvisioner>();
    }

    [Fact]
    public void AddBsftSandboxTenants_OnItsOwn_RegistersNoRegistryToFallBackOn()
    {
        // No default registry on purpose: an in-memory one would look like the seam working and would
        // hand out a colliding identifier the first time the process recycled. So the provisioner
        // cannot even be constructed until the host decides, and the startup check says so by name.
        using var provider = new ServiceCollection().AddBsftSandboxTenants().BuildServiceProvider();

        provider.GetService<ISandboxTenantRegistry>().Should().BeNull();
    }

    [Fact]
    public void AddBsftSandboxTenants_WithACallback_AppliesIt()
    {
        using var provider = new ServiceCollection()
            .AddBsftSandboxTenants(options => options.IdPrefix = "try")
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftSandboxTenantOptions>>().Value.IdPrefix.Should().Be("try");
    }

    [Fact]
    public void AddBsftSandboxTenants_WithASectionAndACallback_RunsTheCallbackAfterTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Bsft:SandboxTenants:IdPrefix"] = "from-section",
                ["Bsft:SandboxTenants:MaxProvisionAttempts"] = "9",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddBsftSandboxTenants(configuration, options => options.IdPrefix = "from-code")
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BsftSandboxTenantOptions>>().Value;

        options.IdPrefix.Should().Be("from-code");
        options.MaxProvisionAttempts.Should().Be(9);
    }

    [Fact]
    public void AddBsftSandboxTenants_WithUnusableOptions_RefusesThemWhenRead()
    {
        using var provider = new ServiceCollection()
            .AddBsftSandboxTenants(options => options.MaxProvisionAttempts = 0)
            .BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<BsftSandboxTenantOptions>>().Value;

        read.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle().Which.Should().Contain(nameof(BsftSandboxTenantOptions.MaxProvisionAttempts));
    }

    [Fact]
    public async Task Validate_AHostWithoutARegistry_IsRefusedAtStart()
    {
        var services = new ServiceCollection();

        services.AddBsftSandboxTenants();

        await using var provider = services.BuildServiceProvider();

        var result = await Check(provider).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("ISandboxTenantRegistry");
    }

    [Fact]
    public async Task Validate_AHostThatBroughtOne_Passes()
    {
        var services = new ServiceCollection();

        services.AddBsftSandboxTenants();
        services.AddSingleton(Substitute.For<ISandboxTenantRegistry>());

        await using var provider = services.BuildServiceProvider();

        (await Check(provider).ValidateAsync()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithoutAScopeFactory_Refuses()
    {
        var act = () => new SandboxTenantStartupValidator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("scopes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateOptions_ABlankPrefix_IsRefused(string prefix)
    {
        Validate(options => options.IdPrefix = prefix).FailureMessage.Should().Contain("IdPrefix");
    }

    [Fact]
    public void ValidateOptions_ALifetimeOrRetryCeilingOfZero_IsRefused()
    {
        Validate(options => options.DefaultLifetime = TimeSpan.Zero).FailureMessage.Should().Contain("DefaultLifetime");
        Validate(options => options.MaxProvisionAttempts = 0).FailureMessage.Should().Contain("MaxProvisionAttempts");
    }

    [Fact]
    public void ValidateOptions_TheShippedSettings_Pass()
    {
        new BsftSandboxTenantOptionsValidator()
            .Validate(name: null, new BsftSandboxTenantOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ValidateOptions_WithoutOptions_Refuses()
    {
        var act = () => new BsftSandboxTenantOptionsValidator().Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void AddBsftSandboxTenants_WithoutItsArguments_Refuses()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var noServices = () => SandboxTenantServiceCollectionExtensions.AddBsftSandboxTenants(null!);
        var noServicesWithSection = () =>
            SandboxTenantServiceCollectionExtensions.AddBsftSandboxTenants(null!, configuration);
        var noConfiguration = () => services.AddBsftSandboxTenants((IConfiguration)null!);

        noServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        noServicesWithSection.Should().Throw<ArgumentNullException>().WithParameterName("services");
        noConfiguration.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    private static Bsft.Core.IStartupValidator Check(IServiceProvider provider)
        => provider.GetServices<Bsft.Core.IStartupValidator>().OfType<SandboxTenantStartupValidator>().Single();

    private static ValidateOptionsResult Validate(Action<BsftSandboxTenantOptions> configure)
    {
        var options = new BsftSandboxTenantOptions();

        configure(options);

        return new BsftSandboxTenantOptionsValidator().Validate(name: null, options);
    }
}
