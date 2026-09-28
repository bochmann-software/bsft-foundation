using System.Net;
using Bsft.Http.Egress.DependencyInjection;
using Bsft.Http.Egress.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class RegistrationTests
{
    [Fact]
    public void AddBsftHttpEgress_RegistersTheSeams()
    {
        var provider = new ServiceCollection().AddBsftHttpEgress().BuildServiceProvider();

        provider.GetRequiredService<IEgressAddressResolver>().Should().BeOfType<SystemEgressAddressResolver>();
        provider.GetRequiredService<IOutboundAddressPolicy>().Should().BeOfType<OutboundAddressPolicy>();
        provider.GetRequiredService<IOutboundConnectGuard>().Should().BeOfType<OutboundConnectGuard>();
        provider.GetRequiredService<EgressGuardHandler>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftHttpEgress_TheDefaultsAreTheStrictOnes()
    {
        var provider = new ServiceCollection().AddBsftHttpEgress().BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BsftHttpEgressOptions>>().Value;

        options.AllowPrivateNetworks.Should().BeFalse();
        options.AllowInsecureUrls.Should().BeFalse();
        options.AllowedHosts.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftHttpEgress_TheDelegateShapesTheOptions()
    {
        var provider = new ServiceCollection()
            .AddBsftHttpEgress(options => options.AllowPrivateNetworks = true)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftHttpEgressOptions>>().Value
            .AllowPrivateNetworks.Should().BeTrue();
    }

    [Fact]
    public void AddBsftHttpEgress_BindsTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftHttpEgressOptions.SectionName}:AllowInsecureUrls"] = "true",
                [$"{BsftHttpEgressOptions.SectionName}:AllowedHosts:0"] = "relay.internal",
            })
            .Build();

        var provider = new ServiceCollection()
            .AddBsftHttpEgress(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BsftHttpEgressOptions>>().Value;

        options.AllowInsecureUrls.Should().BeTrue();
        options.AllowedHosts.Should().ContainSingle().Which.Should().Be("relay.internal");
    }

    [Fact]
    public void AddBsftHttpEgress_TheDelegateRunsAfterTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftHttpEgressOptions.SectionName}:AllowInsecureUrls"] = "true",
            })
            .Build();

        var provider = new ServiceCollection()
            .AddBsftHttpEgress(configuration, options => options.AllowInsecureUrls = false)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftHttpEgressOptions>>().Value
            .AllowInsecureUrls.Should().BeFalse();
    }

    [Fact]
    public void AddBsftHttpEgress_AnAllowListEntryThatMatchesNothing_FailsTheStartupValidation()
    {
        var provider = new ServiceCollection()
            .AddBsftHttpEgress(options => options.AllowedHosts.Add("https://relay.internal"))
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftHttpEgressOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddBsftHttpEgress_LeavesAHostsOwnSeamsAlone()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEgressAddressResolver>(new StubEgressAddressResolver());

        var provider = services.AddBsftHttpEgress().BuildServiceProvider();

        provider.GetRequiredService<IEgressAddressResolver>().Should().BeOfType<StubEgressAddressResolver>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("configuration")]
    public void AddBsftHttpEgress_WithoutAServiceCollection_Throws(string? overload)
    {
        var act = overload is null
            ? () => HttpEgressServiceCollectionExtensions.AddBsftHttpEgress(null!)
            : new Action(() => HttpEgressServiceCollectionExtensions.AddBsftHttpEgress(
                null!, new ConfigurationBuilder().Build()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftHttpEgress_WithoutAConfiguration_Throws()
    {
        var act = () => new ServiceCollection().AddBsftHttpEgress(configuration: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftHttpEgressGuard_WithoutABuilder_Throws()
    {
        var act = () => HttpEgressHttpClientBuilderExtensions.AddBsftHttpEgressGuard(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftHttpEgressGuard_PutsTheRequestTimeHandlerOnTheChainAndTheGuardOnTheSocket()
    {
        var services = new ServiceCollection();
        services.AddBsftHttpEgress();
        services.AddHttpClient("guarded").AddBsftHttpEgressGuard(handler => handler.AllowAutoRedirect = false);

        var builder = BuildChain(services, "guarded");

        builder.AdditionalHandlers.Should().ContainSingle().Which.Should().BeOfType<EgressGuardHandler>();

        var primary = builder.PrimaryHandler.Should().BeOfType<SocketsHttpHandler>().Subject;

        primary.AllowAutoRedirect.Should().BeFalse();
        primary.ConnectCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task AddBsftHttpEgressGuard_AGuardedClientNeverConnectsToAnInternalAddress()
    {
        // The whole chain, driven the way a host drives it: a name that passes every check the URL is
        // subject to, and resolves to the metadata address at the moment the connection is opened.
        var services = new ServiceCollection();
        services.AddSingleton<IEgressAddressResolver>(
            new StubEgressAddressResolver([IPAddress.Parse("169.254.169.254")]));
        services.AddBsftHttpEgress();
        services.AddHttpClient("guarded").AddBsftHttpEgressGuard();

        var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("guarded");

        var act = () => client.GetAsync(new Uri("https://metadata.example.test/latest/"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithInnerException<HttpRequestException, OutboundAddressRejectedException>();
    }

    [Fact]
    public void AddBsftHttpEgressGuard_WithThePredicateSayingNo_LeavesTheChainUntouched()
    {
        var services = new ServiceCollection();
        services.AddBsftHttpEgress();
        services.AddHttpClient("unguarded").AddBsftHttpEgressGuard(_ => false);

        var builder = BuildChain(services, "unguarded");

        builder.AdditionalHandlers.Should().BeEmpty();
        builder.PrimaryHandler.Should().BeOfType<HttpClientHandler>();
    }

    [Fact]
    public void AddBsftHttpEgressGuard_AsksTheSwitchOncePerChain()
    {
        // Once, because two questions could be answered differently and the client would come out half
        // guarded: the request-time handler in place and the connect-time half quietly missing.
        var asked = 0;

        var services = new ServiceCollection();
        services.AddBsftHttpEgress();
        services.AddHttpClient("guarded").AddBsftHttpEgressGuard(_ =>
        {
            asked++;

            return true;
        });

        BuildChain(services, "guarded");

        asked.Should().Be(1);
    }

    [Fact]
    public void AddBsftHttpEgressGuard_WithoutAPredicate_Throws()
    {
        var services = new ServiceCollection();
        services.AddBsftHttpEgress();

        var act = () => services.AddHttpClient("guarded").AddBsftHttpEgressGuard(isEnabled: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftHttpEgressGuard_WithAPredicateButNoBuilder_Throws()
    {
        var act = () => HttpEgressHttpClientBuilderExtensions.AddBsftHttpEgressGuard(null!, _ => true);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AddBsftHttpEgressGuard_WithThePredicateSayingNo_LeavesTheClientUnguarded()
    {
        // Both halves stand down together: the request-time handler passes through, and the primary
        // handler is the one the client factory would have given the client anyway.
        var services = new ServiceCollection();
        services.AddSingleton<IEgressAddressResolver>(
            new StubEgressAddressResolver([IPAddress.Parse("169.254.169.254")]));
        services.AddBsftHttpEgress();
        services.AddHttpClient("unguarded").AddBsftHttpEgressGuard(_ => false);

        var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("unguarded");
        client.Timeout = TimeSpan.FromMilliseconds(250);

        var act = () => client.GetAsync(new Uri("http://127.0.0.1:1/latest/"));

        await act.Should().NotThrowAsync<OutboundAddressRejectedException>();
    }

    [Fact]
    public async Task AddBsftHttpEgressGuard_WithThePredicateSayingYes_GuardsTheClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEgressAddressResolver>(
            new StubEgressAddressResolver([IPAddress.Parse("169.254.169.254")]));
        services.AddBsftHttpEgress();
        services.AddHttpClient("guarded").AddBsftHttpEgressGuard(_ => true);

        var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("guarded");

        var act = () => client.GetAsync(new Uri("https://metadata.example.test/latest/"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithInnerException<HttpRequestException, OutboundAddressRejectedException>();
    }

    [Fact]
    public async Task AddBsftHttpEgressGuard_AGuardedClientRefusesPlainHttpBeforeItConnects()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEgressAddressResolver>(new StubEgressAddressResolver());
        services.AddBsftHttpEgress();
        services.AddHttpClient("guarded").AddBsftHttpEgressGuard();

        var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("guarded");

        var act = () => client.GetAsync(new Uri("http://hooks.example.test/deliver"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithInnerException<HttpRequestException, OutboundAddressRejectedException>();
    }

    /// <summary>
    /// Drives the chain the way the client factory does, and hands back what it built.
    /// </summary>
    /// <param name="services">The registrations to build from.</param>
    /// <param name="name">The client name.</param>
    /// <returns>The builder the recorded actions ran against.</returns>
    private static HttpMessageHandlerBuilderStub BuildChain(IServiceCollection services, string name)
    {
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name);

        var builder = new HttpMessageHandlerBuilderStub(provider);

        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }

        return builder;
    }

    /// <summary>
    /// Stands in for the builder the client factory drives, so the actions a registration recorded can
    /// be run and inspected without building a client and sending a request through it.
    /// </summary>
    private sealed class HttpMessageHandlerBuilderStub : HttpMessageHandlerBuilder
    {
        public HttpMessageHandlerBuilderStub(IServiceProvider services) => Services = services;

        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services { get; }

        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
