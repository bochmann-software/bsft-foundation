using Bsft.Data.MultiTenancy;
using Bsft.EventAggregator;
using Bsft.Localization.EFCore;
using Bsft.Localization.Tests.Fakes;
using Bsft.Localization.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Net;
using Xunit;
using static Bsft.Localization.Tests.Support.LocalizationHost;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.Localization.DependencyInjection;
using Bsft.Localization.EFCore.DependencyInjection;

namespace Bsft.Localization.Tests.Bundles;

public class TranslationBundleEndpointsTests
{
    [Fact]
    public void MapBsftLocalization_NullEndpoints_ThrowsArgumentNullException()
    {
        IEndpointRouteBuilder endpoints = null!;

        var act = () => endpoints.MapBsftLocalization();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("endpoints");
    }

    [Fact]
    public async Task MapBsftLocalization_WithStubFactory_FallbackBranchReturnsOk()
    {
        // The fallback branches: when the factory is not BsftStringLocalizerFactory the endpoints
        // answer with empty results rather than failing.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    // Stub factory (not BsftStringLocalizerFactory) -> hits fallback branches
                    services.AddSingleton<IStringLocalizerFactory>(Substitute.For<IStringLocalizerFactory>());
                    // No AddBsftLocalization here: the route prefix and the module-filter bounds
                    // come from IOptions, which falls back to the defaults when nothing configured it.
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapBsftLocalization());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        var localizationResponse = await client.GetAsync("/api/localization");
        localizationResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var languagesResponse = await client.GetAsync("/api/localization/languages");
        languagesResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var bundleResponse = await client.GetAsync("/api/localization/en");
        bundleResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        await host.StopAsync();
    }

    [Fact]
    public async Task MapBsftLocalization_WithRealFactory_BsftBranches_ReturnOk()
    {
        // The real branches: the factory is BsftStringLocalizerFactory and the localizer it hands
        // back is a BsftStringLocalizer.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftEventAggregator();
                    services.AddBsftTranslations(b => b.ForLocale("en", lb => lb.Add("greeting", "Hello")));
                    // Register the real BsftStringLocalizerFactory
                    services.AddBsftLocalization(options =>
                    {
                        options.DefaultLocale = "en";
                        options.FallbackLocale = "en";
                    });
                    // Register a test ITranslationDbContext (required by BsftStringLocalizer)
                    services.AddDbContext<TestTranslationDbContext>(opt =>
                        opt.UseInMemoryDatabase("TranslationEndpointTest_" + Guid.NewGuid()));
                    services.AddBsftLocalizationEFCore<TestTranslationDbContext>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapBsftLocalization());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // /api/localization - BsftStringLocalizerFactory branch (lines 33-38)
        var localizationResponse = await client.GetAsync("/api/localization");
        localizationResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // /api/localization/languages - BsftStringLocalizerFactory branch (lines 54-58)
        var languagesResponse = await client.GetAsync("/api/localization/languages");
        languagesResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // /api/localization/{locale} - BsftStringLocalizer branch (lines 75-77)
        var bundleResponse = await client.GetAsync("/api/localization/en");
        bundleResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        await host.StopAsync();
    }

    [Fact]
    public async Task MapBsftLocalization_ReturnEndpointsBuilder()
    {
        // MapBsftLocalization hands the builder back, so it composes with the other Map calls.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddSingleton<IStringLocalizerFactory>(Substitute.For<IStringLocalizerFactory>());
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var result = endpoints.MapBsftLocalization();
                        result.Should().NotBeNull();
                    });
                });
            })
            .StartAsync();

        host.Should().NotBeNull();
        await host.StopAsync();
    }

    [Theory]
    [InlineData("..%2F..%2Fetc")]
    [InlineData("en_US")]
    [InlineData("qq")]
    [InlineData("a-very-long-thing-nobody-speaks-at-all")]
    public async Task Bundle_ALocaleThatIsNotALanguageTagTheRuntimeKnows_IsRefused(string locale)
    {
        // The segment decides what is resolved and what is cached for half an hour, so an
        // anonymous caller must not be able to name an unbounded number of them.
        using var host = await StartAsync(new MutableTenantContext());

        var response = await host.GetTestClient().GetAsync("/api/localization/" + locale);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("language tag");
    }

    [Fact]
    public async Task Bundle_ARefusedLocale_IsNotEchoedBackIntoTheAnswer()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await host.GetTestClient().GetAsync("/api/localization/%3Cscript%3E");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("script");
    }

    [Fact]
    public async Task Bundle_ALanguageTagTheRuntimeKnows_IsStillServed()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await host.GetTestClient().GetAsync("/api/localization/en");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("greeting");
    }

    [Fact]
    public async Task Bundle_IsResolvedForTheTenantOfTheRequestItIsServing()
    {
        // The tenant context is scoped state the host writes onto the request's own scope, so a
        // localizer that opened a scope beside the request would read an unresolved context and
        // serve every tenant the global bundle.
        var database = "TranslationBundleRequestTenant_" + Guid.NewGuid();
        using var host = await StartHostWithRequestTenantAsync(database);

        AddTranslation(host, key: "greeting", value: "Hello Acme", tenantId: "acme");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/localization/en");
        request.Headers.Add(TenantHeader, "acme");

        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Hello Acme");
    }

    private const string TenantHeader = "X-Tenant";

    /// <summary>
    /// Starts a host whose tenant is resolved onto the request's scope, the way the framework's own
    /// tenant middleware resolves it.
    /// </summary>
    /// <param name="databaseName">The store to use.</param>
    /// <returns>The started host.</returns>
    private static async Task<IHost> StartHostWithRequestTenantAsync(string databaseName)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftEventAggregator();
                    services.AddBsftTranslations(seed => seed.ForLocale("en", locale => locale.Add("greeting", "Hello")));
                    services.AddBsftLocalization(options =>
                    {
                        options.DefaultLocale = "en";
                        options.FallbackLocale = "en";
                    });
                    services.AddDbContext<TestTranslationDbContext>(opt => opt.UseInMemoryDatabase(databaseName));
                    services.AddBsftLocalizationEFCore<TestTranslationDbContext>();
                    services.AddScoped<TenantContext>();
                    services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.RequestServices.GetRequiredService<TenantContext>()
                            .SetTenantId(context.Request.Headers[TenantHeader].ToString());

                        await next(context);
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapBsftLocalization());
                });
            })
            .StartAsync();
    }
}
