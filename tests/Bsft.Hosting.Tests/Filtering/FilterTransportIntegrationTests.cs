using Bsft.Correlation;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using System.Net.Http.Json;
using System.Net;
using Xunit;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests.Filtering;

/// <summary>
/// Drives the filter transport end to end through the MVC model-binding pipeline: the provider that
/// <c>AddBsftFilterTransport</c> registers must bind the <c>filter</c> query parameter into
/// <c>PagedRequest.Filter</c>, and a rejected filter must surface as the middleware's 400.
/// </summary>
public class FilterTransportIntegrationTests
{
    private sealed record Echo(string? FilterType, int Page, int PageSize, int? SortCount);

    private static async Task<IHost> CreateHostAsync(Action<BsftFilterTransportOptions>? configure = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddScoped<ICorrelationContext>(_ =>
                    {
                        var ctx = Substitute.For<ICorrelationContext>();
                        ctx.CorrelationId.Returns("test-correlation-id");
                        return ctx;
                    });
                    services.AddBsftFilterTransport(configure);
                    services.AddControllers()
                        .AddApplicationPart(typeof(FilterTransportIntegrationTests).Assembly);
                })
                .Configure(app =>
                {
                    app.UseMiddleware<BsftExceptionMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();
    }

    [Fact]
    public async Task ValidFilter_BindsIntoPagedRequestFilter()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();
        var filter = """{ "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" }""";

        var response = await client.GetAsync("/api/filter-echo?filter=" + Uri.EscapeDataString(filter) + "&page=2&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<Echo>();
        echo!.FilterType.Should().Be(nameof(ComparisonNode));
        echo.Page.Should().Be(2);
        echo.PageSize.Should().Be(10);

        await host.StopAsync();
    }

    [Fact]
    public async Task NestedFilter_BindsWholeTree()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();
        var filter = """
        { "$type": "and", "and": [
            { "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" },
            { "$type": "comparison", "field": "Age", "op": "gte", "value": 18 }
        ]}
        """;

        var response = await client.GetAsync("/api/filter-echo?filter=" + Uri.EscapeDataString(filter));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<Echo>();
        echo!.FilterType.Should().Be(nameof(AndNode));

        await host.StopAsync();
    }

    [Fact]
    public async Task NoFilter_LeavesFilterNull_AndListStillBinds()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/filter-echo?page=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<Echo>();
        echo!.FilterType.Should().BeNull();
        echo.Page.Should().Be(3);

        await host.StopAsync();
    }

    [Fact]
    public async Task MalformedFilter_Returns400ProblemDetails()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/filter-echo?filter=" + Uri.EscapeDataString("{ not json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Validation Error");

        await host.StopAsync();
    }

    [Fact]
    public async Task TooDeepFilter_Returns400()
    {
        using var host = await CreateHostAsync(o => { o.MaxDepth = 2; o.MaxJsonReaderDepth = 64; });
        var client = host.GetTestClient();
        var filter = """
        { "$type": "and", "and": [
            { "$type": "and", "and": [
                { "$type": "comparison", "field": "Name", "op": "eq", "value": "x" }
            ]}
        ]}
        """;

        var response = await client.GetAsync("/api/filter-echo?filter=" + Uri.EscapeDataString(filter));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await host.StopAsync();
    }
}
