using Bsft.Hosting.Cors;
using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bsft.Hosting.Tests.Cors;

/// <summary>
/// Pins the CORS response headers of the configurations that existed before the evaluator was added, so a
/// change to how the policy is built shows up here as a changed header (<c>Vary: Origin</c> in particular).
/// </summary>
public class CorsDefaultResponseTests
{
    private const string A = "https://a.example";
    private const string B = "https://b.example";

    private static async Task<Dictionary<string, string>> CorsHeadersAsync(
        Action<BsftCorsOptions> configure, string? origin, bool isPreflight = false)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddBsftCors(configure))
                .Configure(app =>
                {
                    app.UseBsftCors();
                    app.Run(_ => Task.CompletedTask);
                }))
            .StartAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(isPreflight ? HttpMethod.Options : HttpMethod.Get, "/");
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        if (isPreflight)
        {
            request.Headers.Add("Access-Control-Request-Method", "PUT");
            request.Headers.Add("Access-Control-Request-Headers", "x-custom");
        }

        using var response = await client.SendAsync(request);

        return response.Headers
            .Where(header => header.Key.StartsWith("Access-Control-", StringComparison.Ordinal) || header.Key == "Vary")
            .ToDictionary(header => header.Key, header => string.Join(",", header.Value));
    }

    private static Dictionary<string, string> Expected(params (string Name, string Value)[] headers) =>
        headers.ToDictionary(header => header.Name, header => header.Value);

    [Fact]
    public async Task SimpleRequest_WithSingleOrigin_AllowsItWithoutVary()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), A);

        headers.Should().BeEquivalentTo(Expected(("Access-Control-Allow-Origin", A)));
    }

    [Fact]
    public async Task SimpleRequest_WithTwoOrigins_AllowsTheMatchingOneWithVary()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A, B), B);

        headers.Should().BeEquivalentTo(Expected(("Access-Control-Allow-Origin", B), ("Vary", "Origin")));
    }

    [Fact]
    public async Task SimpleRequest_WithWildcardEntry_AllowsAnyOrigin()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins("*"), A);

        headers.Should().BeEquivalentTo(Expected(("Access-Control-Allow-Origin", "*")));
    }

    [Fact]
    public async Task SimpleRequest_WithWildcardEntryMixedWithANamedOrigin_TreatsTheWildcardAsAnOrdinaryEntry()
    {
        var named = await CorsHeadersAsync(o => o.AllowOrigins("*", A), A);
        var other = await CorsHeadersAsync(o => o.AllowOrigins("*", A), B);

        named.Should().BeEquivalentTo(Expected(("Access-Control-Allow-Origin", A), ("Vary", "Origin")));
        other.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithAnyOrigin_AllowsAnyOrigin()
    {
        var headers = await CorsHeadersAsync(o => o.AllowAny(), A);

        headers.Should().BeEquivalentTo(Expected(("Access-Control-Allow-Origin", "*")));
    }

    [Fact]
    public async Task SimpleRequest_WithAnyOriginAndCredentials_IgnoresTheCredentials()
    {
        var headers = await CorsHeadersAsync(o => o.AllowAny().AllowCredentials(), A);

        headers.Should().BeEquivalentTo(Expected(("Access-Control-Allow-Origin", "*")));
    }

    [Fact]
    public async Task SimpleRequest_WithSingleOriginAndCredentials_AllowsItWithoutVary()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A).AllowCredentials(), A);

        headers.Should().BeEquivalentTo(
            Expected(("Access-Control-Allow-Origin", A), ("Access-Control-Allow-Credentials", "true")));
    }

    [Fact]
    public async Task SimpleRequest_WithNonMatchingOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), B);

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithoutOriginHeader_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), origin: null);

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithEmptyOriginsAndARequestOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(_ => { }, A);

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithADifferentlyCasedOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), "https://A.example");

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithATrailingSlashOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), A + "/");

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithAConfiguredTrailingSlashOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A + "/"), A);

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleRequest_WithTheNullOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), "null");

        headers.Should().BeEmpty();
    }

    [Fact]
    public async Task Preflight_WithSingleOrigin_AllowsItWithoutVary()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), A, isPreflight: true);

        headers.Should().BeEquivalentTo(Expected(
            ("Access-Control-Allow-Origin", A),
            ("Access-Control-Allow-Methods", "PUT"),
            ("Access-Control-Allow-Headers", "x-custom")));
    }

    [Fact]
    public async Task Preflight_WithSingleOriginAndCredentials_AllowsItWithoutVary()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A).AllowCredentials(), A, isPreflight: true);

        headers.Should().BeEquivalentTo(Expected(
            ("Access-Control-Allow-Origin", A),
            ("Access-Control-Allow-Credentials", "true"),
            ("Access-Control-Allow-Methods", "PUT"),
            ("Access-Control-Allow-Headers", "x-custom")));
    }

    [Fact]
    public async Task Preflight_WithTwoOrigins_AllowsTheMatchingOneWithVary()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A, B), A, isPreflight: true);

        headers.Should().BeEquivalentTo(Expected(
            ("Access-Control-Allow-Origin", A),
            ("Access-Control-Allow-Methods", "PUT"),
            ("Access-Control-Allow-Headers", "x-custom"),
            ("Vary", "Origin")));
    }

    [Fact]
    public async Task Preflight_WithAnyOrigin_AllowsAnyOrigin()
    {
        var headers = await CorsHeadersAsync(o => o.AllowAny(), A, isPreflight: true);

        headers.Should().BeEquivalentTo(Expected(
            ("Access-Control-Allow-Origin", "*"),
            ("Access-Control-Allow-Methods", "PUT"),
            ("Access-Control-Allow-Headers", "x-custom")));
    }

    [Fact]
    public async Task Preflight_WithNonMatchingOrigin_SendsNoCorsHeaders()
    {
        var headers = await CorsHeadersAsync(o => o.AllowOrigins(A), B, isPreflight: true);

        headers.Should().BeEmpty();
    }
}
