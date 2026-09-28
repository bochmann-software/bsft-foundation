using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class MultiTenancyRegistrationTests
{
    [Fact]
    public void WithHeader_RegistersServicesAndReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        result.Should().BeSameAs(services);
        services.Should().Contain(sd => sd.ServiceType == typeof(TenantContext));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantContext));
        services.Should().Contain(sd => sd.ServiceType == typeof(TenantSaveChangesInterceptor));
        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantResolver)
            && sd.ImplementationType == typeof(HeaderTenantResolver));
    }

    [Fact]
    public void WithClaim_RegistersClaimResolver()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromClaim());

        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantResolver)
            && sd.ImplementationType == typeof(ClaimTenantResolver));
    }

    [Fact]
    public void WithSubdomain_RegistersSubdomainResolver()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromSubdomain());

        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantResolver)
            && sd.ImplementationType == typeof(SubdomainTenantResolver));
    }

    [Fact]
    public void WithCustomResolver_RegistersCustomResolver()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveUsing<HeaderTenantResolver>());

        services.Should().Contain(sd => sd.ServiceType == typeof(ITenantResolver)
            && sd.ImplementationType == typeof(HeaderTenantResolver));
    }

    [Fact]
    public void NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftDataMultiTenancy(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void DefaultOptions_TakeTheFrameworkDefaults()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        var options = Resolve(services);
        options.MultiTenancy.HeaderName.Should().Be(DataDefaults.TenantHeaderName);
        options.MultiTenancy.ClaimType.Should().Be(DataDefaults.TenantClaimType);
    }

    [Fact]
    public void HeaderNameStatedInCode_ReachesTheOptions()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader("X-Account"));

        Resolve(services).MultiTenancy.HeaderName.Should().Be("X-Account");
    }

    [Fact]
    public void ClaimTypeStatedInCode_ReachesTheOptions()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromClaim("org"));

        Resolve(services).MultiTenancy.ClaimType.Should().Be("org");
    }

    [Fact]
    public void Configuration_BindsTheSection()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(
            Configuration(("Bsft:Data:MultiTenancy:HeaderName", "X-From-Config")),
            cfg => cfg.ResolveFromHeader());

        Resolve(services).MultiTenancy.HeaderName.Should().Be("X-From-Config");
    }

    [Fact]
    public void NameStatedInCode_WinsOverTheSection()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(
            Configuration(("Bsft:Data:MultiTenancy:HeaderName", "X-From-Config")),
            cfg => cfg.ResolveFromHeader("X-From-Code"));

        Resolve(services).MultiTenancy.HeaderName.Should().Be("X-From-Code");
    }

    [Fact]
    public void Configuration_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(null!, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Configuration_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftDataMultiTenancy(Configuration(), _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void Configuration_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(Configuration(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void BlankHeaderName_FailsTheRegistrationCall()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader(" "));

        act.Should().Throw<InvalidOperationException>().WithMessage("*HeaderName*must not be blank*");
    }

    [Fact]
    public void HeaderNameThatIsNotAnHttpToken_FailsTheRegistrationCall()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader("X Tenant"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*valid HTTP header name*");
    }

    [Fact]
    public void BlankClaimType_FailsTheRegistrationCall()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromClaim(" "));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ClaimType*must not be blank*");
    }

    [Fact]
    public void InvalidSection_FailsTheRegistrationCall()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftDataMultiTenancy(
            Configuration(("Bsft:Data:MultiTenancy:ClaimType", "  ")),
            cfg => cfg.ResolveFromClaim());

        act.Should().Throw<InvalidOperationException>().WithMessage("*ClaimType*must not be blank*");
    }

    [Fact]
    public void TheValidatorIsRegisteredSoTheSameRulesRunOnStart()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        services.BuildServiceProvider()
            .GetServices<IValidateOptions<BsftDataOptions>>()
            .Should().ContainSingle(v => v is BsftDataOptionsValidator);
    }

    private static BsftDataOptions Resolve(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IOptions<BsftDataOptions>>().Value;

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
