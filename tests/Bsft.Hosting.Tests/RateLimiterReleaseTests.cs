using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using Bsft.Correlation.DependencyInjection;
using Bsft.Hosting.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.Tests;

public class RateLimiterReleaseTests
{
    /// <summary>
    /// A disposed host built with the Bsft defaults is collected. The rate limiting middleware's endpoint
    /// limiter runs a timer until it is disposed, and that timer held the middleware, the pipeline and the
    /// container of every stopped host for the rest of the process (dotnet/aspnetcore#66434).
    /// </summary>
    [Fact]
    public async Task DisposedHost_WithTheBsftDefaults_IsCollected()
    {
        var services = await StartUseAndDisposeAsync(withGlobalLimiter: false);

        Collect(services).Should().BeFalse("a disposed host must not stay reachable through a limiter's timer");
    }

    /// <summary>
    /// A global limiter whose partitioner reaches the container (as one reading the host's options does)
    /// does not keep the disposed host alive either: the container disposes it with the middleware's own.
    /// </summary>
    [Fact]
    public async Task DisposedHost_WithAGlobalLimiter_IsCollected()
    {
        var services = await StartUseAndDisposeAsync(withGlobalLimiter: true);

        Collect(services).Should().BeFalse("a disposed host must not stay reachable through its global limiter's timer");
    }

    [Fact]
    public void Dispose_DisposesTheEndpointAndTheGlobalLimiter()
    {
        using var provider = RateLimitingServices(out var global);
        var endpoint = EndpointLimiter(provider);

        provider.GetRequiredService<RateLimiterRelease>().Dispose();

        Disposed(endpoint).Should().BeTrue();
        Disposed(global).Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheEndpointAndTheGlobalLimiter()
    {
        await using var provider = RateLimitingServices(out var global);
        var endpoint = EndpointLimiter(provider);

        await provider.GetRequiredService<RateLimiterRelease>().DisposeAsync();

        Disposed(endpoint).Should().BeTrue();
        Disposed(global).Should().BeTrue();
    }

    /// <summary>A pipeline built after the container began disposing gets its limiters disposed at once.</summary>
    [Fact]
    public void UseRateLimiter_AfterDispose_DisposesTheLimitersAtOnce()
    {
        using var provider = RateLimitingServices(out var global);
        provider.GetRequiredService<RateLimiterRelease>().Dispose();

        var endpoint = EndpointLimiter(provider);

        Disposed(endpoint).Should().BeTrue();
        Disposed(global).Should().BeTrue();
    }

    /// <summary>The pipeline is built twice; the global limiter both share is kept, and disposed, once.</summary>
    [Fact]
    public void Dispose_WithTwoPipelines_DisposesBothEndpointLimiters()
    {
        using var provider = RateLimitingServices(out var global);
        var first = EndpointLimiter(provider);
        var second = EndpointLimiter(provider);

        provider.GetRequiredService<RateLimiterRelease>().Dispose();

        Disposed(first).Should().BeTrue();
        Disposed(second).Should().BeTrue();
        Disposed(global).Should().BeTrue();
    }

    /// <summary>
    /// The endpoint limiter is where the release reads it, on every target framework the suite runs on. An
    /// ASP.NET Core that keeps it elsewhere fails here, loudly, while a host only logs a warning.
    /// </summary>
    [Fact]
    public void FindEndpointLimiter_OnTheBuiltPipeline_FindsTheLimiter()
    {
        using var provider = RateLimitingServices(out _);
        var logger = new RecordingLogger<RateLimiterRelease>();
        var release = new RateLimiterRelease(provider.GetRequiredService<IOptions<RateLimiterOptions>>(), logger);

        var limiter = release.FindEndpointLimiter(BuildPipeline(provider).Target, provider);

        limiter.Should().NotBeNull();
        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FindEndpointLimiter_WithoutTheMiddleware_ReturnsNullAndWarns(bool withInstance)
    {
        using var provider = RateLimitingServices(out _);
        var logger = new RecordingLogger<RateLimiterRelease>();
        var release = new RateLimiterRelease(provider.GetRequiredService<IOptions<RateLimiterOptions>>(), logger);

        var limiter = release.FindEndpointLimiter(withInstance ? new object() : null, provider);

        limiter.Should().BeNull();
        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(withInstance ? "System.Object" : "no instance");
    }

    /// <summary>
    /// A container that has the middleware registered (the shape of the upstream fix, in which the middleware
    /// is a disposable singleton) disposes it itself, so a limiter the release cannot find is no warning.
    /// </summary>
    [Fact]
    public void FindEndpointLimiter_WhenTheContainerOwnsTheMiddleware_ReturnsNullWithoutAWarning()
    {
        var middlewareType = typeof(RateLimiterOptions).Assembly.GetType(RateLimiterRelease.MiddlewareTypeName)!;
        var services = new ServiceCollection();
        services.AddSingleton(middlewareType, _ => new object());
        using var provider = services.BuildServiceProvider();
        var logger = new RecordingLogger<RateLimiterRelease>();
        var release = new RateLimiterRelease(Options.Create(new RateLimiterOptions()), logger);

        var limiter = release.FindEndpointLimiter(new object(), provider);

        limiter.Should().BeNull();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void UseBsftRateLimiting_WithoutAddBsftRateLimiting_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(_ => { });
        using var provider = services.BuildServiceProvider();
        IApplicationBuilder app = new ApplicationBuilder(provider);

        var act = () => app.UseBsftRateLimiting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddBsftRateLimiting*");
    }

    private static ServiceProvider RateLimitingServices(out PartitionedRateLimiter<HttpContext> global)
    {
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetNoLimiter("all"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftRateLimiting();
        services.Configure<RateLimiterOptions>(options => options.GlobalLimiter = limiter);
        global = limiter;

        return services.BuildServiceProvider();
    }

    private static PartitionedRateLimiter<HttpContext> EndpointLimiter(ServiceProvider provider)
        => provider.GetRequiredService<RateLimiterRelease>().FindEndpointLimiter(BuildPipeline(provider).Target, provider)!;

    private static RequestDelegate BuildPipeline(IServiceProvider provider)
    {
        IApplicationBuilder app = new ApplicationBuilder(provider);
        app.UseBsftRateLimiting();

        return app.Build();
    }

    private static bool Disposed(PartitionedRateLimiter<HttpContext> limiter)
    {
        try
        {
            using var lease = limiter.AttemptAcquire(new DefaultHttpContext());
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static bool Collect(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        return reference.IsAlive;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> StartUseAndDisposeAsync(bool withGlobalLimiter)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Production");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                    services.AddBsftDefaults(defaults => defaults.DisableHttpsRedirection());
                    services.AddBsftCorrelation();
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                    if (withGlobalLimiter)
                    {
                        services.AddOptions<RateLimiterOptions>().Configure<IServiceProvider>((options, provider) =>
                            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                                RateLimitPartition.GetNoLimiter(provider.GetHashCode().ToString(CultureInfo.InvariantCulture))));
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseBsftDefaults();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/limited", () => Results.Ok())
                        .RequireRateLimiting(HostingDefaults.RateLimitingPolicyName));
                });
            })
            .StartAsync();

        // A request through the policy, so a partition limiter with a timer of its own exists as well.
        using (var response = await host.GetTestClient().GetAsync("/limited"))
            response.EnsureSuccessStatusCode();

        var services = new WeakReference(host.Services);
        await host.StopAsync();
        host.Dispose();

        return services;
    }
}
