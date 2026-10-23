using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Bsft.Hosting.DependencyInjection;
using Bsft.Correlation;

namespace Bsft.Hosting.Tests;

public class RateLimitingPolicyTests
{
    private const string CallerHeader = "X-Test-Caller";
    private const string GuestHeader = "X-Test-Guest";
    private const string SessionHeader = "X-Test-Session";

    [Fact]
    public async Task PrincipalPolicy_TwoGuestsOneAddress_CountSeparately()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:PartitionBy"] = "Principal",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:WindowSeconds"] = "600"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        var response1 = await Get(client, remoteIp: "203.0.113.1", guest: "g1");
        var response2 = await Get(client, remoteIp: "203.0.113.1", guest: "g1");
        var response3 = await Get(client, remoteIp: "203.0.113.1", guest: "g2");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PrincipalPolicy_AnonymousRequests_CountAgainstTheirAddress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:PartitionBy"] = "Principal",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:WindowSeconds"] = "600"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        var response1 = await Get(client, remoteIp: "203.0.113.2", guest: null);
        var response2 = await Get(client, remoteIp: "203.0.113.2", guest: null);
        var response3 = await Get(client, remoteIp: "203.0.113.3", guest: null);

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomSelector_TwoSessionsOneAddress_CountSeparately()
    {
        using var host = await CustomSelectorPolicyHostAsync();
        var client = host.GetTestClient();

        var response1 = await Get(client, remoteIp: "203.0.113.4", guest: "g1", session: "s1");
        var response2 = await Get(client, remoteIp: "203.0.113.4", guest: "g1", session: "s1");
        var response3 = await Get(client, remoteIp: "203.0.113.4", guest: "g1", session: "s2");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChainedAddressAndGuestLimiters_FreshIPWithThreeGuests_ThirdGuestHits429()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:PartitionBy"] = "ClientAddress",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:PermitLimit"] = "2",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:WindowSeconds"] = "600",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:PartitionBy"] = "Principal",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:WindowSeconds"] = "600"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        var response1 = await Get(client, remoteIp: "203.0.113.5", guest: "g1", policy: "chain");
        var response2 = await Get(client, remoteIp: "203.0.113.5", guest: "g2", policy: "chain");
        var response3 = await Get(client, remoteIp: "203.0.113.5", guest: "g3", policy: "chain");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task PerPrincipalAndGlobalConcurrency_ConcurrentRequestsHitLimits_RetryAfterFromGlobalLimiter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:0:Kind"] = "Concurrency",
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:0:PartitionBy"] = "Principal",
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:Kind"] = "Concurrency",
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:PartitionBy"] = "Global",
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:PermitLimit"] = "2",
                ["Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:RetryAfterSeconds"] = "7"
            })
            .Build();

        var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftRateLimiting(configuration);
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress =
                            context.Request.Headers.TryGetValue(CallerHeader, out var caller)
                                ? IPAddress.Parse(caller.ToString())
                                : null;
                        await next(context);
                    });

                    app.Use(async (context, next) =>
                    {
                        var guestHeader = context.Request.Headers.TryGetValue(GuestHeader, out var guest)
                            ? guest.ToString()
                            : null;

                        if (guestHeader is not null)
                        {
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, guestHeader)
                            };
                            var identity = new ClaimsIdentity(claims, "test");
                            context.User = new ClaimsPrincipal(identity);
                        }

                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints =>
                    {
                        var group = endpoints.MapGroup("/uploads").RequireRateLimiting("upload-streams");
                        group.MapPatch("/{id}", async () =>
                        {
                            entered.Release();
                            await release.Task;
                            return Results.Ok();
                        });
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // g1's first upload holds the guest's only permit and one of the two global ones.
        var uploadA = Patch(client, "g1");
        (await entered.WaitAsync(EnterTimeout)).Should().BeTrue("upload A reaches the handler");

        using (var uploadB = await Patch(client, "g1"))
        {
            uploadB.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "g1 has one upload in flight already");
            uploadB.Headers.RetryAfter!.ToString().Should().Be("1", "the per-guest limiter has no RetryAfterSeconds of its own");
        }

        // g2's upload takes the second global permit.
        var uploadC = Patch(client, "g2");
        (await entered.WaitAsync(EnterTimeout)).Should().BeTrue("upload C reaches the handler");

        using (var uploadD = await Patch(client, "g3"))
        {
            uploadD.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "both global permits are held");
            uploadD.Headers.RetryAfter!.ToString().Should().Be("7");
        }

        release.SetResult();
        using (var responseA = await uploadA)
            responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var responseC = await uploadC)
            responseC.StatusCode.Should().Be(HttpStatusCode.OK);

        using var afterwards = await Patch(client, "g1");
        afterwards.StatusCode.Should().Be(HttpStatusCode.OK, "the finished uploads handed their permits back");
    }

    private static readonly TimeSpan EnterTimeout = TimeSpan.FromSeconds(30);

    private static async Task<HttpResponseMessage> Patch(HttpClient client, string guest)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/uploads/1");
        request.Headers.Add(CallerHeader, "203.0.113.6");
        request.Headers.Add(GuestHeader, guest);

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task RejectionBody_DefaultPolicy_ContainsErrorCodeAndRetryAfter()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftRateLimiting(o =>
                    {
                        o.PermitLimit = 1;
                        o.WindowSeconds = 600;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/limited", () => Results.Ok())
                        .RequireRateLimiting(HostingDefaults.RateLimitingPolicyName));
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        using var resp1 = await client.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        using var resp2 = await client.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        resp2.Headers.RetryAfter!.ToString().Should().Be("600");
        resp2.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await resp2.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(429);
        root.GetProperty("title").GetString().Should().Be("Too many requests.");
        root.GetProperty("type").GetString().Should().Be("https://httpstatuses.io/429");
        root.GetProperty("errorCode").GetString().Should().Be("RATE_LIMITED");
        root.GetProperty("retryAfterSeconds").GetInt32().Should().Be(600);
        root.GetProperty("detail").GetString().Should().Contain("600");
        root.TryGetProperty("correlationId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RejectionBody_WithProblemDetailsService_CustomizationApplied()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["customized"] = true);
                    services.AddBsftRateLimiting(o =>
                    {
                        o.PermitLimit = 1;
                        o.WindowSeconds = 600;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");
                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/limited", () => Results.Ok())
                        .RequireRateLimiting(HostingDefaults.RateLimitingPolicyName));
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        await client.SendAsync(req1);

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        using var resp2 = await client.SendAsync(req2);

        resp2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        resp2.Headers.RetryAfter!.ToString().Should().Be("600");

        var body = await resp2.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("errorCode").GetString().Should().Be("RATE_LIMITED");
        root.GetProperty("customized").GetBoolean().Should().BeTrue();
        root.GetProperty("status").GetInt32().Should().Be(429);
    }

    [Fact]
    public async Task CorrelationId_WhenServiceRegistered_IncludedInBody()
    {
        var fakeCorrelationContext = new FakeCorrelationContext { CorrelationId = "corr-1" };

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddSingleton<ICorrelationContext>(fakeCorrelationContext);
                    services.AddBsftRateLimiting(o =>
                    {
                        o.PermitLimit = 1;
                        o.WindowSeconds = 600;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/limited", () => Results.Ok())
                        .RequireRateLimiting(HostingDefaults.RateLimitingPolicyName));
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        await client.SendAsync(req1);

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        using var resp2 = await client.SendAsync(req2);

        var body = await resp2.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("correlationId").GetString().Should().Be("corr-1");
    }

    [Fact]
    public async Task HostSetOnRejected_WhenRegisteredBeforeAddBsftRateLimiting_IsPreserved()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddRateLimiter(o => o.OnRejected = (ctx, _) =>
                    {
                        ctx.HttpContext.Response.Headers["X-Host-Rejected"] = "1";
                        return ValueTask.CompletedTask;
                    });
                    services.AddBsftRateLimiting(o =>
                    {
                        o.PermitLimit = 1;
                        o.WindowSeconds = 600;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/limited", () => Results.Ok())
                        .RequireRateLimiting(HostingDefaults.RateLimitingPolicyName));
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        await client.SendAsync(req1);

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/limited");
        using var resp2 = await client.SendAsync(req2);

        resp2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        resp2.Headers.GetValues("X-Host-Rejected").FirstOrDefault().Should().Be("1");
        resp2.Headers.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task RejectionMetric_CountsRejectionsByPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:PartitionBy"] = "Principal",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:per-guest:Limiters:0:WindowSeconds"] = "600"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        var measurements = new List<long>();
        var collectedTags = new List<KeyValuePair<string, object?>>();

        using var listener = new MeterListener();
        listener.InstrumentPublished += (instrument, listener) =>
        {
            if (instrument.Name == HostingDefaults.RateLimitRejectionsInstrument &&
                instrument.Meter.Name == "Bsft.Hosting" &&
                ReferenceEquals(instrument.Meter.Scope, meterFactory))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
            foreach (var tag in tags)
                collectedTags.Add(tag);
        });

        listener.Start();

        // First request succeeds, second is rejected
        await Get(client, remoteIp: "203.0.113.11", guest: "g1");
        await Get(client, remoteIp: "203.0.113.11", guest: "g1");

        measurements.Should().HaveCount(1);
        measurements[0].Should().Be(1);
        collectedTags.Should().ContainSingle().Which.Key.Should().Be("policy");
        collectedTags[0].Value.Should().Be("per-guest");
    }

    [Fact]
    public async Task SlidingWindowPolicy_SecondRequest_IsRejectedWithRetryAfter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:sliding:Limiters:0:Kind"] = "SlidingWindow",
                ["Bsft:Hosting:RateLimiting:Policies:sliding:Limiters:0:PartitionBy"] = "ClientAddress",
                ["Bsft:Hosting:RateLimiting:Policies:sliding:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:sliding:Limiters:0:WindowSeconds"] = "600",
                ["Bsft:Hosting:RateLimiting:Policies:sliding:Limiters:0:SegmentsPerWindow"] = "2"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        var response1 = await Get(client, remoteIp: "203.0.113.12", policy: "sliding");
        var response2 = await Get(client, remoteIp: "203.0.113.12", policy: "sliding");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response2.Headers.RetryAfter.Should().NotBeNull();
        int.Parse(response2.Headers.RetryAfter!.ToString()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TokenBucketPolicy_SecondRequest_IsRejectedWithTheReplenishmentPeriod()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:tokens:Limiters:0:Kind"] = "TokenBucket",
                ["Bsft:Hosting:RateLimiting:Policies:tokens:Limiters:0:PartitionBy"] = "ClientAddress",
                ["Bsft:Hosting:RateLimiting:Policies:tokens:Limiters:0:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:tokens:Limiters:0:TokensPerPeriod"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:tokens:Limiters:0:WindowSeconds"] = "600"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        var response1 = await Get(client, remoteIp: "203.0.113.13", policy: "tokens");
        var response2 = await Get(client, remoteIp: "203.0.113.13", policy: "tokens");

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response2.Headers.RetryAfter!.ToString().Should().Be("600");
    }

    [Fact]
    public void ConfiguredRateLimitPolicies_ProviderDisposed_RefusesToApply()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting(o =>
        {
            o.Policies["test"] = new RateLimitPolicyOptions();
            o.Policies["test"].Limiters.Add(new PolicyLimiterOptions
            {
                Kind = RateLimiterKind.FixedWindow,
                PartitionBy = RateLimitPartitionKind.Global,
                PermitLimit = 1,
                WindowSeconds = 600
            });
        });

        using var provider = services.BuildServiceProvider();

        // Trigger policy building by resolving options
        _ = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value;

        var policies = provider.GetRequiredService<ConfiguredRateLimitPolicies>();

        var limiterOptions = new Microsoft.AspNetCore.RateLimiting.RateLimiterOptions();
        policies.ApplyTo(limiterOptions);

        provider.Dispose();

        var act = () => policies.ApplyTo(new Microsoft.AspNetCore.RateLimiting.RateLimiterOptions());
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ChainPolicy_RejectedByALaterTimeBasedLimiter_SpendsTwoPermitsOfTheEarlierOne()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:PartitionBy"] = "Global",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:PermitLimit"] = "3",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:0:WindowSeconds"] = "600",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:Kind"] = "FixedWindow",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:PartitionBy"] = "Principal",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:PermitLimit"] = "1",
                ["Bsft:Hosting:RateLimiting:Policies:chain:Limiters:1:WindowSeconds"] = "600"
            })
            .Build();

        using var host = await PolicyHostAsync(configuration);
        var client = host.GetTestClient();

        // g1: 200, then 429
        var response1A = await Get(client, remoteIp: "203.0.113.14", guest: "g1", policy: "chain");
        var response1B = await Get(client, remoteIp: "203.0.113.14", guest: "g1", policy: "chain");

        response1A.StatusCode.Should().Be(HttpStatusCode.OK);
        response1B.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // g2: check if gets 200 or 429 (expected 429 per documented behavior)
        var response2 = await Get(client, remoteIp: "203.0.113.14", guest: "g2", policy: "chain");

        // This assertion reflects the expected behavior per the spec: global limiter has 3 permits total,
        // g1 gets 1 (OK), then asks again and is rejected (reject also uses a permit or asks twice), so 2 permits spent.
        // Then g2's request asks the global limiter and gets rejected (at 3/3 permits spent).
        response2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static async Task<IHost> PolicyHostAsync(IConfiguration configuration)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftRateLimiting(configuration);
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress =
                            context.Request.Headers.TryGetValue(CallerHeader, out var caller)
                                ? IPAddress.Parse(caller.ToString())
                                : null;
                        await next(context);
                    });

                    app.Use(async (context, next) =>
                    {
                        var guestHeader = context.Request.Headers.TryGetValue(GuestHeader, out var guest)
                            ? guest.ToString()
                            : null;

                        if (guestHeader is not null)
                        {
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, guestHeader)
                            };
                            var identity = new ClaimsIdentity(claims, "test");
                            context.User = new ClaimsPrincipal(identity);
                        }

                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/test", () => Results.Ok())
                            .RequireRateLimiting("per-guest");
                        endpoints.MapGet("/sliding", () => Results.Ok())
                            .RequireRateLimiting("sliding");
                        endpoints.MapGet("/tokens", () => Results.Ok())
                            .RequireRateLimiting("tokens");
                        endpoints.MapGet("/chain", () => Results.Ok())
                            .RequireRateLimiting("chain");
                    });
                });
            })
            .StartAsync();
    }

    private static async Task<IHost> CustomSelectorPolicyHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftRateLimiting(o =>
                    {
                        o.PartitionKeySelectors["session"] = ctx => ctx.User.FindFirst("sid")?.Value;
                        o.Policies["per-session"] = new RateLimitPolicyOptions();
                        o.Policies["per-session"].Limiters.Add(new PolicyLimiterOptions
                        {
                            Kind = RateLimiterKind.FixedWindow,
                            PartitionBy = RateLimitPartitionKind.Custom,
                            PartitionKeySelector = "session",
                            PermitLimit = 1,
                            WindowSeconds = 600
                        });
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress =
                            context.Request.Headers.TryGetValue(CallerHeader, out var caller)
                                ? IPAddress.Parse(caller.ToString())
                                : null;
                        await next(context);
                    });

                    app.Use(async (context, next) =>
                    {
                        var guestHeader = context.Request.Headers.TryGetValue(GuestHeader, out var guest)
                            ? guest.ToString()
                            : null;

                        if (guestHeader is not null)
                        {
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, guestHeader)
                            };

                            var sessionHeader = context.Request.Headers.TryGetValue(SessionHeader, out var session)
                                ? session.ToString()
                                : null;

                            if (sessionHeader is not null)
                                claims.Add(new Claim("sid", sessionHeader));

                            var identity = new ClaimsIdentity(claims, "test");
                            context.User = new ClaimsPrincipal(identity);
                        }

                        await next(context);
                    });

                    app.UseRouting();
                    app.UseBsftRateLimiting();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/test", () => Results.Ok())
                        .RequireRateLimiting("per-session"));
                });
            })
            .StartAsync();
    }

    private static async Task<HttpResponseMessage> Get(
        HttpClient client,
        string? remoteIp = null,
        string? guest = null,
        string? session = null,
        string policy = "per-guest")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{(policy == "per-guest" ? "test" : policy)}");

        if (remoteIp is not null)
            request.Headers.Add(CallerHeader, remoteIp);

        if (guest is not null)
            request.Headers.Add(GuestHeader, guest);

        if (session is not null)
            request.Headers.Add(SessionHeader, session);

        var response = await client.SendAsync(request);
        return response;
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; set; } = string.Empty;
    }
}
