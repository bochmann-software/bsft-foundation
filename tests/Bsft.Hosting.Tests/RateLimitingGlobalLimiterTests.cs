using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests;

/// <summary>
/// The optional global limiter, the time span windows, the optional default policy and the custom key
/// prefix, each with the path on which nothing is configured and the behaviour is the one it always was.
/// </summary>
public class RateLimitingGlobalLimiterTests
{
    private const string KeyHeader = "X-Test-Key";

    [Fact]
    public async Task Defaults_NothingConfigured_NoGlobalLimiterAndTheDefaultPolicyLimitsPerAddress()
    {
        using var host = await HostAsync(configuration: null, o => o.PermitLimit = 1);
        var client = host.GetTestClient();

        host.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter.Should().BeNull();
        (await Get(client, "/default", "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/default", "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Get(client, "/open", "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Defaults_DefaultPolicyWindow_IsSixtySeconds()
    {
        using var host = await HostAsync(configuration: null, o => o.PermitLimit = 1);
        var client = host.GetTestClient();

        await Get(client, "/default", "203.0.113.2");
        var rejected = await Get(client, "/default", "203.0.113.2");

        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
    }

    [Fact]
    public async Task Window_BoundFromTimeSpanString_TakesPrecedenceOverWindowSeconds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Window"] = "00:10:00"
            })
            .Build();

        using var host = await HostAsync(configuration);
        var client = host.GetTestClient();

        await Get(client, "/default", "203.0.113.3");
        var rejected = await Get(client, "/default", "203.0.113.3");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(61, 600);
    }

    [Fact]
    public async Task Window_OfANamedPolicyLimiter_IsBoundFromATimeSpanString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:timed:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:timed:Limiters:0:Window"] = "00:10:00"
            })
            .Build();

        using var host = await HostAsync(configuration);
        var client = host.GetTestClient();

        await Get(client, "/timed", "203.0.113.4");
        var rejected = await Get(client, "/timed", "203.0.113.4");

        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(61, 600);
    }

    [Fact]
    public async Task EnableDefaultPolicy_False_RegistersNoDefaultPolicy()
    {
        using var host = await HostAsync(configuration: null, o => o.EnableDefaultPolicy = false);
        var client = host.GetTestClient();

        var act = () => Get(client, "/default", "203.0.113.5");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await Get(client, "/timed", "203.0.113.5")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GlobalLimiter_WithCustomSelector_CountsEveryEndpointPerSelectedKey()
    {
        using var host = await HostAsync(configuration: null, o =>
        {
            o.PartitionKeySelectors["key"] = context => context.Request.Headers[KeyHeader].ToString();
            o.GlobalLimiter = new PolicyLimiterOptions
            {
                PartitionBy = RateLimitPartitionKind.Custom,
                PartitionKeySelector = "key",
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(10)
            };
        });
        var client = host.GetTestClient();

        (await Get(client, "/open", "203.0.113.6", key: "a")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/timed", "203.0.113.7", key: "a")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await Get(client, "/open", "203.0.113.8", key: "a");
        (await Get(client, "/open", "203.0.113.6", key: "b")).StatusCode.Should().Be(HttpStatusCode.OK);

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task GlobalLimiter_BoundFromConfiguration_LimitsPerClientAddress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:GlobalLimiter:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:GlobalLimiter:Window"] = "00:00:30"
            })
            .Build();

        using var host = await HostAsync(configuration);
        var client = host.GetTestClient();

        (await Get(client, "/open", "203.0.113.9")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/open", "203.0.113.9")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Get(client, "/open", "203.0.113.10")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomPartitionKeyPrefix_Empty_StillSeparatesCustomKeys()
    {
        using var host = await HostAsync(configuration: null, o =>
        {
            o.CustomPartitionKeyPrefix = "";
            o.PartitionKeySelectors["key"] = context => context.Request.Headers[KeyHeader].ToString();
            o.GlobalLimiter = new PolicyLimiterOptions
            {
                PartitionBy = RateLimitPartitionKind.Custom,
                PartitionKeySelector = "key",
                PermitLimit = 1
            };
        });
        var client = host.GetTestClient();

        (await Get(client, "/open", "203.0.113.11", key: "a")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/open", "203.0.113.11", key: "a")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Get(client, "/open", "203.0.113.11", key: "b")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Task<IHost> HostAsync(IConfiguration? configuration, Action<BsftRateLimitingOptions>? configure = null)
        => new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    if (configuration is null)
                    {
                        services.AddBsftRateLimiting(o =>
                        {
                            o.Policies["timed"] = new RateLimitPolicyOptions { Limiters = { new PolicyLimiterOptions { PermitLimit = 100 } } };
                            configure?.Invoke(o);
                        });
                    }
                    else
                    {
                        services.AddBsftRateLimiting(configuration, o => configure?.Invoke(o));
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress =
                            context.Request.Headers.TryGetValue("X-Test-Caller", out var caller)
                                ? IPAddress.Parse(caller.ToString())
                                : null;
                        await next(context);
                    });
                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/open", () => Results.Ok());
                        endpoints.MapGet("/default", () => Results.Ok())
                            .RequireRateLimiting(HostingDefaults.RateLimitingPolicyName);
                        endpoints.MapGet("/timed", () => Results.Ok()).RequireRateLimiting("timed");
                    });
                });
            })
            .StartAsync();

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string remoteIp, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Caller", remoteIp);
        if (key is not null)
            request.Headers.Add(KeyHeader, key);

        return client.SendAsync(request);
    }
}
