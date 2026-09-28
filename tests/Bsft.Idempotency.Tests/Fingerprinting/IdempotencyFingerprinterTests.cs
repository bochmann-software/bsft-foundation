using Bsft.Data.MultiTenancy;
using Bsft.Idempotency.Fingerprinting;
using Bsft.Idempotency.Tests.Fakes;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace Bsft.Idempotency.Tests.Fingerprinting;

public class IdempotencyFingerprinterTests
{
    [Fact]
    public async Task ComputeAsync_TheSameRequestTwice_HasTheSameScopeAndFingerprint()
    {
        var first = await Compute(HttpContexts.Create());
        var second = await Compute(HttpContexts.Create());

        first.Should().Be(second);
    }

    [Fact]
    public async Task ComputeAsync_ADifferentBody_ChangesTheFingerprintButNotTheScope()
    {
        var first = await Compute(HttpContexts.Create(body: "{\"a\":1}"));
        var second = await Compute(HttpContexts.Create(body: "{\"a\":2}"));

        first.Fingerprint.Should().NotBe(second.Fingerprint);
        first.Scope.Should().Be(second.Scope);
    }

    [Fact]
    public async Task ComputeAsync_ADifferentMethodOrContentType_ChangesTheFingerprint()
    {
        var baseline = await Compute(HttpContexts.Create());
        var method = await Compute(HttpContexts.Create(method: "PATCH"));
        var contentType = await Compute(HttpContexts.Create(contentType: "text/plain"));

        method.Fingerprint.Should().NotBe(baseline.Fingerprint);
        contentType.Fingerprint.Should().NotBe(baseline.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_AMatchedRoute_IsHashedAsThePatternRatherThanThePath()
    {
        var first = await Compute(HttpContexts.Create(path: "/api/orders/1", routePattern: "/api/orders/{id}"));
        var second = await Compute(HttpContexts.Create(path: "/api/orders/2", routePattern: "/api/orders/{id}"));

        first.Fingerprint.Should().Be(second.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_TwoDifferentRoutePatterns_Differ()
    {
        var first = await Compute(HttpContexts.Create(routePattern: "/api/orders"));
        var second = await Compute(HttpContexts.Create(routePattern: "/api/invoices"));

        first.Fingerprint.Should().NotBe(second.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_NothingMatched_FallsBackToTheRawPath()
    {
        var first = await Compute(HttpContexts.Create(path: "/api/orders/1"));
        var second = await Compute(HttpContexts.Create(path: "/api/orders/2"));

        first.Fingerprint.Should().NotBe(second.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_AnEndpointThatIsNotARoute_FallsBackToTheRawPath()
    {
        var withPlainEndpoint = HttpContexts.Create(path: "/api/orders");

        withPlainEndpoint.SetEndpoint(HttpContexts.PlainEndpoint());

        var plain = await Compute(withPlainEndpoint);
        var none = await Compute(HttpContexts.Create(path: "/api/orders"));

        plain.Fingerprint.Should().Be(none.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_TheQueryInAnotherOrder_IsTheSameRequest()
    {
        var first = await Compute(HttpContexts.Create(query: "?b=2&a=1"));
        var second = await Compute(HttpContexts.Create(query: "?a=1&b=2"));

        first.Fingerprint.Should().Be(second.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_ADifferentQuery_IsADifferentRequest()
    {
        var first = await Compute(HttpContexts.Create(query: "?a=1"));
        var second = await Compute(HttpContexts.Create(query: "?a=2"));
        var none = await Compute(HttpContexts.Create());

        first.Fingerprint.Should().NotBe(second.Fingerprint);
        first.Fingerprint.Should().NotBe(none.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_ARepeatedQueryKey_ContributesEveryValue()
    {
        var twice = await Compute(HttpContexts.Create(query: "?a=1&a=2"));
        var once = await Compute(HttpContexts.Create(query: "?a=1"));

        twice.Fingerprint.Should().NotBe(once.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_ADifferentTenant_IsADifferentCallerAndADifferentRequest()
    {
        var first = await Compute(HttpContexts.Create(services: Services("alpha")));
        var second = await Compute(HttpContexts.Create(services: Services("beta")));

        first.Scope.Should().NotBe(second.Scope);
        first.Fingerprint.Should().NotBe(second.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_AnUnresolvedTenant_IsNotTheTenantWhoseIdIsBlank()
    {
        var unresolved = await Compute(HttpContexts.Create(services: Services(null, isResolved: false)));
        var blank = await Compute(HttpContexts.Create(services: Services(string.Empty)));

        unresolved.Scope.Should().NotBe(blank.Scope);
    }

    [Fact]
    public async Task ComputeAsync_AResolvedTenantWithoutAnId_IsTreatedAsNoTenant()
    {
        var idless = await Compute(HttpContexts.Create(services: Services(null)));
        var none = await Compute(HttpContexts.Create(services: Services(null, isResolved: false)));

        idless.Scope.Should().Be(none.Scope);
    }

    [Fact]
    public async Task ComputeAsync_NoTenantContextAtAll_IsTheSameAsAnUnresolvedOne()
    {
        var noServices = await Compute(HttpContexts.Create());
        var noContext = await Compute(HttpContexts.Create(services: new ServiceCollection().BuildServiceProvider()));

        noServices.Scope.Should().Be(noContext.Scope);
    }

    [Fact]
    public async Task ComputeAsync_ADifferentSubject_IsADifferentCaller()
    {
        var first = await Compute(HttpContexts.Create(user: User(("sub", "alice"))));
        var second = await Compute(HttpContexts.Create(user: User(("sub", "bob"))));
        var anonymous = await Compute(HttpContexts.Create());

        first.Scope.Should().NotBe(second.Scope);
        first.Scope.Should().NotBe(anonymous.Scope);
    }

    [Fact]
    public async Task ComputeAsync_ASubjectThatArrivedAsANameIdentifier_IsStillTheSubject()
    {
        var nameIdentifier = await Compute(
            HttpContexts.Create(user: User((ClaimTypes.NameIdentifier, "alice"))));
        var anonymous = await Compute(HttpContexts.Create());

        nameIdentifier.Scope.Should().NotBe(anonymous.Scope);
    }

    [Fact]
    public async Task ComputeAsync_ASubClaim_WinsOverTheNameIdentifier()
    {
        var both = await Compute(
            HttpContexts.Create(user: User(("sub", "alice"), (ClaimTypes.NameIdentifier, "bob"))));
        var subOnly = await Compute(HttpContexts.Create(user: User(("sub", "alice"))));

        both.Scope.Should().Be(subOnly.Scope);
    }

    [Fact]
    public async Task ComputeAsync_ADifferentClient_IsADifferentCaller()
    {
        var first = await Compute(HttpContexts.Create(user: User(("client_id", "mobile"))));
        var second = await Compute(HttpContexts.Create(user: User(("client_id", "web"))));

        first.Scope.Should().NotBe(second.Scope);
    }

    [Fact]
    public async Task ComputeAsync_TwoApiKeysBehindOneAddress_AreDifferentCallers()
    {
        // Two service credentials of one tenant leaving through one egress address: without the key
        // id both would scope to that address and the second report would be served the first's
        // stored response.
        var first = await Compute(
            HttpContexts.Create(user: User(("bsft:api-key", "key-a")), remoteAddress: "203.0.113.7"));
        var second = await Compute(
            HttpContexts.Create(user: User(("bsft:api-key", "key-b")), remoteAddress: "203.0.113.7"));

        first.Scope.Should().NotBe(second.Scope);
    }

    [Fact]
    public async Task ComputeAsync_AnApiKeyCaller_IsNotNamedByItsConnection()
    {
        var first = await Compute(
            HttpContexts.Create(user: User(("bsft:api-key", "key-a")), remoteAddress: "203.0.113.7"));
        var second = await Compute(
            HttpContexts.Create(user: User(("bsft:api-key", "key-a")), remoteAddress: "198.51.100.4"));
        var anonymous = await Compute(HttpContexts.CreateWithoutUser(remoteAddress: "203.0.113.7"));

        first.Scope.Should().Be(second.Scope);
        first.Scope.Should().NotBe(anonymous.Scope);
    }

    [Fact]
    public async Task ComputeAsync_AnApiKeyIdThatReadsLikeAClientId_IsNotThatClient()
    {
        var apiKey = await Compute(HttpContexts.Create(user: User(("bsft:api-key", "mobile"))));
        var client = await Compute(HttpContexts.Create(user: User(("client_id", "mobile"))));

        apiKey.Scope.Should().NotBe(client.Scope);
    }

    [Fact]
    public async Task ComputeAsync_AClientIdBesideAnApiKey_WinsTheClientSegment()
    {
        var both = await Compute(
            HttpContexts.Create(user: User(("client_id", "web"), ("bsft:api-key", "key-a"))));
        var clientOnly = await Compute(HttpContexts.Create(user: User(("client_id", "web"))));

        both.Scope.Should().Be(clientOnly.Scope);
    }

    [Fact]
    public async Task ComputeAsync_ASubjectBesideAnApiKey_KeepsBothSegments()
    {
        var both = await Compute(
            HttpContexts.Create(user: User(("sub", "alice"), ("bsft:api-key", "key-a"))));
        var subjectOnly = await Compute(HttpContexts.Create(user: User(("sub", "alice"))));

        both.Scope.Should().NotBe(subjectOnly.Scope);
    }

    [Fact]
    public async Task ComputeAsync_NoCallerAtAll_IsStillFingerprinted()
    {
        var result = await Compute(HttpContexts.CreateWithoutUser());

        result.Scope.Should().HaveLength(64);
        result.Fingerprint.Should().HaveLength(64);
    }

    [Fact]
    public async Task ComputeAsync_TwoAnonymousCallersOnDifferentConnections_AreDifferentScopes()
    {
        // The security property for an endpoint that is both idempotent and anonymous: three absent
        // caller segments are the same for everybody, so without the connection standing in for the
        // subject the second caller would be handed the first one's stored response.
        var first = await Compute(HttpContexts.CreateWithoutUser(remoteAddress: "203.0.113.7"));
        var second = await Compute(HttpContexts.CreateWithoutUser(remoteAddress: "198.51.100.4"));

        first.Scope.Should().NotBe(second.Scope);
    }

    [Fact]
    public async Task ComputeAsync_OneAnonymousCallerRetrying_KeepsItsScope()
    {
        var first = await Compute(HttpContexts.CreateWithoutUser(remoteAddress: "203.0.113.7"));
        var retry = await Compute(HttpContexts.CreateWithoutUser(remoteAddress: "203.0.113.7"));

        first.Scope.Should().Be(retry.Scope);
    }

    [Fact]
    public async Task ComputeAsync_AnAuthenticatedCaller_IsNotNamedByItsConnection()
    {
        // The address is the fallback identity, not an extra one: a caller with a subject keeps one
        // scope across the two connections a mobile client will use over a retry.
        var first = await Compute(
            HttpContexts.Create(user: User(("sub", "alice")), remoteAddress: "203.0.113.7"));
        var second = await Compute(
            HttpContexts.Create(user: User(("sub", "alice")), remoteAddress: "198.51.100.4"));

        first.Scope.Should().Be(second.Scope);
    }

    [Fact]
    public async Task ComputeAsync_AContributorThatAppendsASegment_ChangesTheFingerprint()
    {
        var baseline = await Compute(HttpContexts.Create());
        var appended = await Compute(
            HttpContexts.Create(),
            new SegmentContributor((_, segments) => [.. segments, "api-version:2"]));

        appended.Fingerprint.Should().NotBe(baseline.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_AContributorThatRemovesASegment_MakesTwoRequestsTheSame()
    {
        IReadOnlyList<string?> WithoutContentType(HttpRequest _, IReadOnlyList<string?> segments) =>
            segments.Where((_, index) => index != 6).ToList();

        var json = await Compute(
            HttpContexts.Create(contentType: "application/json"),
            new SegmentContributor(WithoutContentType));
        var text = await Compute(
            HttpContexts.Create(contentType: "text/plain"),
            new SegmentContributor(WithoutContentType));

        json.Fingerprint.Should().Be(text.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_AContributorThatAnswersNothing_LeavesTheSequenceAlone()
    {
        var baseline = await Compute(HttpContexts.Create());
        var withNullReturn = await Compute(HttpContexts.Create(), new SegmentContributor((_, _) => null));

        withNullReturn.Fingerprint.Should().Be(baseline.Fingerprint);
    }

    [Fact]
    public async Task ComputeAsync_TheContributorSeesTheRequestItIsAdjusting()
    {
        HttpRequest? seen = null;
        var context = HttpContexts.Create();

        await Compute(context, new SegmentContributor((request, segments) =>
        {
            seen = request;

            return segments;
        }));

        seen.Should().BeSameAs(context.Request);
    }

    [Fact]
    public async Task ComputeAsync_ABodyOverTheConfiguredBound_IsRefused()
    {
        var fingerprinter = new IdempotencyFingerprinter(
            Options.Create(new BsftIdempotencyOptions { MaxBodyBytes = 4 }), []);

        await FluentActions
            .Awaiting(() => fingerprinter.ComputeAsync(HttpContexts.Create(body: "{\"a\":1234567}")).AsTask())
            .Should().ThrowAsync<Bsft.Core.PayloadTooLargeException>();
    }

    [Fact]
    public void Constructor_NullOptions_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyFingerprinter(null!, []))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");

    [Fact]
    public void Constructor_NullContributors_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyFingerprinter(Options.Create(new BsftIdempotencyOptions()), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("contributors");

    [Fact]
    public async Task ComputeAsync_NullContext_IsRefused()
    {
        var fingerprinter = new IdempotencyFingerprinter(Options.Create(new BsftIdempotencyOptions()), []);

        await FluentActions.Awaiting(() => fingerprinter.ComputeAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    private static async Task<IdempotencyFingerprintResult> Compute(
        HttpContext context, params IIdempotencyFingerprintContributor[] contributors)
    {
        var fingerprinter = new IdempotencyFingerprinter(
            Options.Create(new BsftIdempotencyOptions()), contributors);

        return await fingerprinter.ComputeAsync(context);
    }

    private static IServiceProvider Services(string? tenantId, bool isResolved = true)
    {
        var services = new ServiceCollection();

        services.AddSingleton<ITenantContext>(new FixedTenantContext(tenantId, isResolved));

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));
}
