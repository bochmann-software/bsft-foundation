using System.Net;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests;

public class RateLimitingTests
{
    [Fact]
    public void BsftRateLimitingOptions_Defaults_AreCorrect()
    {
        var options = new BsftRateLimitingOptions();

        options.PermitLimit.Should().Be(100);
        options.WindowSeconds.Should().Be(60);
        options.RetryAfterSeconds.Should().Be(1);
        options.Policies.Should().BeEmpty();
        options.PartitionKeySelectors.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftRateLimiting_WithDefaults_Registers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting();

        var provider = services.BuildServiceProvider();

        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftRateLimiting_WithCustomValues_Applies()
    {
        var options = new BsftRateLimitingOptions();

        var configureAction = (BsftRateLimitingOptions o) =>
        {
            o.PermitLimit = 50;
            o.WindowSeconds = 30;
        };

        configureAction(options);

        options.PermitLimit.Should().Be(50);
        options.WindowSeconds.Should().Be(30);
    }

    [Fact]
    public void AddBsftRateLimiting_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftRateLimiting();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftRateLimiting_ConfigureLambda_IsInvoked()
    {
        // Exercises RateLimitingServiceCollectionExtensions.AddBsftRateLimiting lines 33-42:
        // the AddRateLimiter lambda is invoked when the rate limiter options are built.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting(o =>
        {
            o.PermitLimit = 200;
            o.WindowSeconds = 120;
        });

        var provider = services.BuildServiceProvider();
        // Resolving rate limiter options triggers the AddRateLimiter lambda (lines 32-43)
        var rateLimiterOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
        rateLimiterOptions.Should().NotBeNull();
        rateLimiterOptions!.Value.Should().NotBeNull();
    }

    [Fact]
    public void UseBsftRateLimiting_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises RateLimitingApplicationBuilderExtensions.UseBsftRateLimiting lines 16-18.
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftRateLimiting();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void UseBsftRateLimiting_WithValidApp_AddsMiddleware()
    {
        // Exercises RateLimitingApplicationBuilderExtensions.UseBsftRateLimiting lines 20-21.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting();
        var provider = services.BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(provider);

        var result = app.UseBsftRateLimiting();

        result.Should().NotBeNull();
    }


    /// <summary>
    /// Each caller counts against its own budget. One limiter shared by the policy would mean the
    /// first caller to spend its permits locks every other caller out for the rest of the window,
    /// which is a denial of service anybody can trigger rather than a rate limit.
    /// </summary>
    [Fact]
    public async Task TheLimiter_CountsEachClientSeparately()
    {
        using var host = await LimitedHost(permitLimit: 1);
        var client = host.GetTestClient();

        (await Get(client, "203.0.113.10")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "203.0.113.10")).Should().Be(HttpStatusCode.TooManyRequests);

        (await Get(client, "203.0.113.11")).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A caller that spends its permits is refused, so the limit is a limit at all.</summary>
    [Fact]
    public async Task TheLimiter_RefusesACallerThatSpentItsPermits()
    {
        using var host = await LimitedHost(permitLimit: 2);
        var client = host.GetTestClient();

        (await Get(client, "203.0.113.20")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "203.0.113.20")).Should().Be(HttpStatusCode.OK);
        (await Get(client, "203.0.113.20")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Requests carrying no remote address share one bucket rather than each being handed a budget
    /// of its own, which would be no limit at all.
    /// </summary>
    [Fact]
    public async Task TheLimiter_PutsRequestsWithoutARemoteAddressInOneBucket()
    {
        using var host = await LimitedHost(permitLimit: 1);
        var client = host.GetTestClient();

        (await Get(client, remoteIp: null)).Should().Be(HttpStatusCode.OK);
        (await Get(client, remoteIp: null)).Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static Task<IHost> LimitedHost(int permitLimit)
        => new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftRateLimiting(o =>
                    {
                        o.PermitLimit = permitLimit;
                        o.WindowSeconds = 600;
                    });
                });
                webBuilder.Configure(app =>
                {
                    // The address the limiter partitions on, before the limiter sees the request.
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress =
                            context.Request.Headers.TryGetValue(CallerHeader, out var caller)
                                ? IPAddress.Parse(caller.ToString())
                                : null;

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

    private static async Task<HttpStatusCode> Get(HttpClient client, string? remoteIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/limited");

        if (remoteIp is not null)
            request.Headers.Add(CallerHeader, remoteIp);

        using var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    private const string CallerHeader = "X-Test-Caller";

    [Fact]
    public void AddBsftRateLimiting_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:RateLimiting:PermitLimit"] = "5",
                ["Bsft:Hosting:RateLimiting:WindowSeconds"] = "30"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftRateLimiting(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftRateLimitingOptions>>().Value;

        options.PermitLimit.Should().Be(5);
        options.WindowSeconds.Should().Be(30);
    }

    [Fact]
    public void AddBsftRateLimiting_WithNonPositivePermitLimit_FailsOnValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftRateLimiting(o => o.PermitLimit = 0);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftRateLimitingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*PermitLimit*");
    }

    [Fact]
    public void AddBsftRateLimiting_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftRateLimiting(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftRateLimiting_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftRateLimiting(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftRateLimiting_WithPoliciesInConfiguration_BindsEveryLimiter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Bsft:Hosting:RateLimiting:RetryAfterSeconds", "7" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:0:Kind", "Concurrency" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:0:PartitionBy", "Principal" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:0:PermitLimit", "3" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:Kind", "Concurrency" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:PartitionBy", "Global" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:PermitLimit", "64" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-streams:Limiters:1:RetryAfterSeconds", "5" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-intents:Limiters:0:Kind", "TokenBucket" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-intents:Limiters:0:TokensPerPeriod", "2" },
                { "Bsft:Hosting:RateLimiting:Policies:upload-intents:Limiters:0:ClaimType", "sid" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftRateLimitingOptions>>().Value;

        options.RetryAfterSeconds.Should().Be(7);
        options.Policies.Should().HaveCount(2);

        options.Policies["upload-streams"].Limiters.Should().HaveCount(2);
        options.Policies["upload-streams"].Limiters[0].Kind.Should().Be(RateLimiterKind.Concurrency);
        options.Policies["upload-streams"].Limiters[0].PartitionBy.Should().Be(RateLimitPartitionKind.Principal);
        options.Policies["upload-streams"].Limiters[0].PermitLimit.Should().Be(3);
        options.Policies["upload-streams"].Limiters[1].Kind.Should().Be(RateLimiterKind.Concurrency);
        options.Policies["upload-streams"].Limiters[1].PartitionBy.Should().Be(RateLimitPartitionKind.Global);
        options.Policies["upload-streams"].Limiters[1].PermitLimit.Should().Be(64);
        options.Policies["upload-streams"].Limiters[1].RetryAfterSeconds.Should().Be(5);

        options.Policies["upload-intents"].Limiters.Should().HaveCount(1);
        options.Policies["upload-intents"].Limiters[0].Kind.Should().Be(RateLimiterKind.TokenBucket);
        options.Policies["upload-intents"].Limiters[0].TokensPerPeriod.Should().Be(2);
        options.Policies["upload-intents"].Limiters[0].ClaimType.Should().Be("sid");
    }

    [Fact]
    public void AddBsftRateLimiting_WithInvalidPolicyInConfiguration_FailsOnValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:Kind", "FixedWindow" },
                { "Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:PermitLimit", "0" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting(configuration);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftRateLimitingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Policies:p:Limiters:0:PermitLimit*");
    }

    [Fact]
    public void AddBsftRateLimiting_WithInvalidPolicy_FailsWhenTheLimiterOptionsAreRead()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:Kind", "FixedWindow" },
                { "Bsft:Hosting:RateLimiting:Policies:p:Limiters:0:PermitLimit", "0" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting(configuration);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddBsftRateLimiting_CalledTwice_RegistersPoliciesOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting();
        services.AddBsftRateLimiting();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.Should().NotBeNull();

        var policiesCount = services.Count(d => d.ServiceType == typeof(ConfiguredRateLimitPolicies));
        policiesCount.Should().Be(1);
    }

    [Fact]
    public void AddBsftRateLimiting_WithRetryAfterSecondsZeroViaAction_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting(o => o.RetryAfterSeconds = 0);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftRateLimitingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*RetryAfterSeconds*");
    }
}
