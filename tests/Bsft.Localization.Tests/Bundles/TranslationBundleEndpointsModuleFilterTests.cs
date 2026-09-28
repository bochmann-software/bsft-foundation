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
/// The module-scoped view of <c>GET /api/localization/{locale}</c>: what it contains, what
/// identifies it and what invalidates it.
/// </summary>
public class TranslationBundleEndpointsModuleFilterTests
{
    [Fact]
    public async Task Filtered_ContainsTheSelectedModulesKeysAndEverythingShared()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var body = await GetBodyAsync(host, "?modules=Orders");

        body.Should().Contain("modules.Orders.displayName").And.Contain("greeting").And.Contain("common.save");
        body.Should().NotContain("modules.Inventory.displayName");
    }

    [Fact]
    public async Task Filtered_ForSeveralModules_ContainsAllOfThem()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var body = await GetBodyAsync(host, "?modules=Orders,Inventory");

        body.Should().Contain("modules.Orders.displayName").And.Contain("modules.Inventory.displayName");
    }

    [Fact]
    public async Task Filtered_ForAModuleNothingIsKeyedUnder_ServesTheSharedKeysWithoutAnError()
    {
        // No existence oracle: an unknown module is a module that owns no keys, not a bad request.
        using var host = await StartAsync(new MutableTenantContext());

        var response = await GetAsync(host, "?modules=Nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("greeting");
        body.Should().NotContain("modules.Orders.displayName");
    }

    [Fact]
    public async Task Filtered_IsServedWithAStrongContentHashTag()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await GetAsync(host, "?modules=Orders");

        EntityTagOf(response).Should().StartWith("\"sha256-").And.EndWith("\"");
    }

    [Theory]
    [InlineData("?modules=Orders,Inventory", "?modules=Inventory,Orders")]
    [InlineData("?modules=Orders,Inventory", "?modules= inventory , ORDERS ")]
    [InlineData("?modules=Orders", "?modules=Orders,Orders")]
    public async Task Filtered_HoweverTheModulesAreNamed_CarriesTheSameTag(string first, string second)
    {
        using var host = await StartAsync(new MutableTenantContext());

        var firstTag = EntityTagOf(await GetAsync(host, first));
        var secondTag = EntityTagOf(await GetAsync(host, second));

        secondTag.Should().Be(firstTag);
    }

    [Fact]
    public async Task Filtered_ForDifferentModules_CarriesDifferentTags()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var orders = EntityTagOf(await GetAsync(host, "?modules=Orders"));
        var inventory = EntityTagOf(await GetAsync(host, "?modules=Inventory"));

        inventory.Should().NotBe(orders);
    }

    [Fact]
    public async Task Filtered_DoesNotShareTheUnfilteredBundlesTag()
    {
        // The two responses have different bodies, so a client holding one must not be told 304 for
        // the other.
        using var host = await StartAsync(new MutableTenantContext());

        var unfiltered = EntityTagOf(await GetAsync(host, string.Empty));
        var filtered = EntityTagOf(await GetAsync(host, "?modules=Orders"));

        filtered.Should().NotBe(unfiltered);
    }

    [Fact]
    public async Task Filtered_MatchingIfNoneMatch_IsAnsweredWithAnEmpty304()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var tag = EntityTagOf(await GetAsync(host, "?modules=Orders"));
        var revalidation = await GetAsync(host, "?modules=Orders", tag);

        revalidation.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await revalidation.Content.ReadAsStringAsync()).Should().BeEmpty();
        EntityTagOf(revalidation).Should().Be(tag);
    }

    [Fact]
    public async Task Filtered_ReorderedIfNoneMatch_IsStillAnsweredWith304()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var tag = EntityTagOf(await GetAsync(host, "?modules=Orders,Inventory"));
        var revalidation = await GetAsync(host, "?modules=Inventory,Orders", tag);

        revalidation.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Filtered_TheUnfilteredTag_DoesNotRevalidateIt()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var unfiltered = EntityTagOf(await GetAsync(host, string.Empty));
        var response = await GetAsync(host, "?modules=Orders", unfiltered);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Filtered_AfterTranslationsUpdatedEvent_ChangesItsTag()
    {
        // The narrowed view hangs off the bundle it was cut from, so the one invalidation path
        // moves both. There is no second mechanism to keep in step.
        var databaseName = NewDatabaseName();
        using var host = await StartAsync(new MutableTenantContext(), databaseName);

        var before = EntityTagOf(await GetAsync(host, "?modules=Orders"));

        AddTranslation(host, key: "modules.Orders.description", value: "Order management");
        await host.Services.GetRequiredService<IEventAggregator>().PublishAsync(new TranslationsUpdatedEvent());

        var response = await GetAsync(host, "?modules=Orders", before);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        EntityTagOf(response).Should().NotBe(before);
        (await response.Content.ReadAsStringAsync()).Should().Contain("modules.Orders.description");
    }

    [Fact]
    public async Task Filtered_WithoutAnInvalidation_StillServesTheOldTag()
    {
        // The negative counterpart: it is the event, not the write, that moves the tag — the same
        // honest limitation the unfiltered bundle has.
        using var host = await StartAsync(new MutableTenantContext());

        var before = EntityTagOf(await GetAsync(host, "?modules=Orders"));
        AddTranslation(host, key: "modules.Orders.description", value: "Order management");

        EntityTagOf(await GetAsync(host, "?modules=Orders")).Should().Be(before);
    }

    [Fact]
    public async Task Filtered_OfAnotherTenant_DoesNotAcceptTheFirstTenantsTag()
    {
        var tenantContext = new MutableTenantContext("acme");
        using var host = await StartAsync(tenantContext);

        var acmeTag = EntityTagOf(await GetAsync(host, "?modules=Orders"));

        tenantContext.SwitchTo("globex");
        var response = await GetAsync(host, "?modules=Orders", acmeTag);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a narrowed view must not cross a tenant boundary");
        EntityTagOf(response).Should().NotBe(acmeTag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?modules=")]
    [InlineData("?modules=,,")]
    [InlineData("?modules=%20")]
    public async Task Unfiltered_AndAnEmptyFilter_AreTheSameResponse(string query)
    {
        // An empty filter takes the unfiltered path unchanged, down to the tag it serves.
        using var host = await StartAsync(new MutableTenantContext());

        var reference = await GetAsync(host, string.Empty);
        var response = await GetAsync(host, query);

        EntityTagOf(response).Should().Be(EntityTagOf(reference));
        (await response.Content.ReadAsStringAsync()).Should().Contain("modules.Inventory.displayName");
    }

    [Fact]
    public async Task Filtered_MoreDistinctModulesThanAllowed_IsRefused()
    {
        using var host = await StartAsync(new MutableTenantContext(), maxFilterModules: 2);

        var response = await GetAsync(host, "?modules=a,b,c");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("distinct modules");
    }

    [Fact]
    public async Task Filtered_AModuleNameLongerThanAllowed_IsRefused()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await GetAsync(host, "?modules=" + new string('a', 65));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Filtered_AModuleNameOutsideTheAdmittedCharacters_IsRefused()
    {
        using var host = await StartAsync(new MutableTenantContext());

        var response = await GetAsync(host, "?modules=Orders%2FInventory");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("letters");
    }

    private const string BundleUrl = "/api/localization/en";

    private static async Task<string> GetBodyAsync(IHost host, string query)
        => await (await GetAsync(host, query)).Content.ReadAsStringAsync();

    private static async Task<HttpResponseMessage> GetAsync(IHost host, string query, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BundleUrl + query);
        if (ifNoneMatch is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);

        return await host.GetTestClient().SendAsync(request);
    }
}
