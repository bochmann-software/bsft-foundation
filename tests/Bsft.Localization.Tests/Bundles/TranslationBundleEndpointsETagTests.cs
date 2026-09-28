using Bsft.EventAggregator;
using Bsft.Localization.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using Xunit;
using static Bsft.Localization.Tests.Support.LocalizationHost;

namespace Bsft.Localization.Tests.Bundles;

/// <summary>
/// The conditional-GET behaviour of <c>GET /api/localization/{locale}</c>.
/// </summary>
public class TranslationBundleEndpointsETagTests
{
    [Fact]
    public async Task Bundle_IsServedWithAStrongContentHashTag()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await host.GetTestClient().GetAsync(BundleUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tag = EntityTagOf(response);
        tag.Should().StartWith("\"sha256-").And.EndWith("\"");
        tag.Should().NotStartWith("W/", "a weak validator cannot be compared strongly");
    }

    [Fact]
    public async Task Bundle_CarriesTheRevalidationPolicy()
    {
        // The bundle depends on the tenant of the request, which the URL does not name. A shared
        // cache keyed on the URL would serve one tenant's translations to the next.
        using var host = await StartAsync(new MutableTenantContext());

        var response = await host.GetTestClient().GetAsync(BundleUrl);

        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.NoCache.Should().BeTrue();
        response.Headers.CacheControl.Public.Should().BeFalse();
    }

    [Fact]
    public async Task Bundle_UnchangedContent_KeepsTheSameTagAcrossRequests()
    {
        using var host = await StartAsync(new MutableTenantContext());
        var client = host.GetTestClient();

        var first = EntityTagOf(await client.GetAsync(BundleUrl));
        var second = EntityTagOf(await client.GetAsync(BundleUrl));

        second.Should().Be(first);
    }

    [Fact]
    public async Task Bundle_MatchingIfNoneMatch_IsAnsweredWithAnEmpty304()
    {
        using var host = await StartAsync(new MutableTenantContext());
        var client = host.GetTestClient();

        var tag = EntityTagOf(await client.GetAsync(BundleUrl));
        var revalidation = await GetAsync(host, tag);

        revalidation.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await revalidation.Content.ReadAsStringAsync()).Should().BeEmpty();
        // A 304 stands in for the response it replaces, so it carries the same validator.
        EntityTagOf(revalidation).Should().Be(tag);
        revalidation.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task Bundle_AfterTranslationsUpdatedEvent_ChangesItsTag()
    {
        var databaseName = NewDatabaseName();
        using var host = await StartAsync(new MutableTenantContext(), databaseName);
        var client = host.GetTestClient();

        var before = EntityTagOf(await client.GetAsync(BundleUrl));

        AddTranslation(host, key: "farewell", value: "Goodbye");
        await host.Services.GetRequiredService<IEventAggregator>()
            .PublishAsync(new TranslationsUpdatedEvent());

        var response = await GetAsync(host, before);

        // The version hangs off the same cache entry the invalidator drops, so the tag moves with
        // the content and the client's old tag no longer revalidates.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        EntityTagOf(response).Should().NotBe(before);
    }

    [Fact]
    public async Task Bundle_WithoutAnInvalidation_StillServesTheOldTag()
    {
        // The negative counterpart of the test above: it is the event, not the write, that moves
        // the tag. Without it the memoised bundle — and its version — are still what is served.
        using var host = await StartAsync(new MutableTenantContext());
        var client = host.GetTestClient();

        var before = EntityTagOf(await client.GetAsync(BundleUrl));
        AddTranslation(host, key: "farewell", value: "Goodbye");

        EntityTagOf(await client.GetAsync(BundleUrl)).Should().Be(before);
    }

    [Fact]
    public async Task Bundle_OfAnotherTenant_DoesNotAcceptTheFirstTenantsTag()
    {
        var tenantContext = new MutableTenantContext("acme");
        using var host = await StartAsync(tenantContext);
        var client = host.GetTestClient();

        AddTranslation(host, tenantId: "acme", key: "greeting", value: "Hello from Acme");
        var acmeTag = EntityTagOf(await client.GetAsync(BundleUrl));

        tenantContext.SwitchTo("globex");
        var response = await GetAsync(host, acmeTag);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a tag must not cross a tenant boundary");
        EntityTagOf(response).Should().NotBe(acmeTag);
    }

    [Fact]
    public async Task Bundle_OfTwoTenantsWithIdenticalContent_StillDiffers()
    {
        // Both tenants resolve to exactly the same translations here. The tag still has to differ:
        // the URL names the locale but never the tenant, so a shared tag would let a client that
        // switched tenant be told 304 for a bundle it is not holding.
        var tenantContext = new MutableTenantContext("acme");
        using var host = await StartAsync(tenantContext);
        var client = host.GetTestClient();

        var acmeTag = EntityTagOf(await client.GetAsync(BundleUrl));

        tenantContext.SwitchTo("globex");
        var globexTag = EntityTagOf(await client.GetAsync(BundleUrl));

        globexTag.Should().NotBe(acmeTag);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("W/")]
    [InlineData("\"unterminated")]
    [InlineData(",,,")]
    public async Task Bundle_MalformedIfNoneMatch_IsIgnoredAndTheBundleIsServed(string ifNoneMatch)
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await GetAsync(host, ifNoneMatch);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("greeting");
    }

    [Fact]
    public async Task Bundle_WildcardIfNoneMatch_IsAnsweredWith304()
    {
        // RFC 9110: "*" asks whether any representation exists, and one does.
        using var host = await StartAsync(new MutableTenantContext());

        var response = await GetAsync(host, "*");

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    private const string BundleUrl = "/api/localization/en";

    private static async Task<HttpResponseMessage> GetAsync(IHost host, string ifNoneMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BundleUrl);
        request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        return await host.GetTestClient().SendAsync(request);
    }
}
