using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting;

/// <summary>
/// Disposes the limiters of ASP.NET Core's rate limiting middleware together with the container that
/// built its pipeline.
/// </summary>
/// <remarks>
/// <para>
/// A partitioned limiter runs a timer until it is disposed. The timer holds the limiter, and the limiter
/// holds whatever its partitioner captured; for the endpoint limiter that is the middleware, the rest of
/// the request pipeline and, through it, the container. Nobody disposes either limiter: the middleware
/// creates its endpoint limiter itself and is not disposable, and <see cref="RateLimiterOptions.GlobalLimiter"/>
/// is handed to it and never given back (dotnet/aspnetcore#66434, dotnet/aspnetcore#69509). In a process
/// that runs one host that costs nothing until shutdown. In a process that starts and stops many, a test
/// suite above all, every stopped host stays reachable for the rest of the process.
/// </para>
/// <para>
/// The global limiter is public on the options and disposed from there. The endpoint limiter is not
/// reachable without reflection: the middleware is internal and keeps the limiter in a private field,
/// and ASP.NET Core offers no hook to supply or replace it. So the middleware is built in a pipeline of
/// its own, where the delegate that pipeline produces is the middleware's <c>Invoke</c> bound to the
/// instance, and the limiter is read from that one field of that one instance.
/// </para>
/// <para>
/// When the limiter is not there, the host still starts. If the container has the middleware registered,
/// which is how the upstream fix (dotnet/aspnetcore#66435) makes it disposable, the container releases it
/// and nothing is lost. Otherwise a warning says that the limiter cannot be released. Refusing to start
/// instead would turn a leak that costs one host nothing into an outage on the next runtime patch. The
/// tests notice the change loudly: they assert that the limiter is found, and that a disposed host is
/// collected, on every target framework.
/// </para>
/// <para>
/// The container owns this type and disposes it, and with it the limiters, after the server has
/// stopped. Disposing the global limiter means it belongs to the host whose options carry it: a host
/// does not share one instance with another host.
/// </para>
/// </remarks>
/// <param name="options">The rate limiter options the middleware reads.</param>
/// <param name="logger">Receives the warning when the endpoint limiter cannot be released.</param>
internal sealed class RateLimiterRelease(IOptions<RateLimiterOptions> options, ILogger<RateLimiterRelease> logger)
    : IAsyncDisposable, IDisposable
{
    /// <summary>The full name of ASP.NET Core's internal rate limiting middleware.</summary>
    internal const string MiddlewareTypeName = "Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware";

    /// <summary>The private field in which the middleware keeps its endpoint limiter.</summary>
    internal const string EndpointLimiterFieldName = "_endpointLimiter";

    private readonly List<PartitionedRateLimiter<HttpContext>> _limiters = [];

    private bool _isDisposed;

    /// <summary>
    /// Adds ASP.NET Core's rate limiting middleware to <paramref name="app"/> and keeps its limiters for
    /// disposal.
    /// </summary>
    /// <param name="app">The application builder.</param>
    internal void UseRateLimiter(IApplicationBuilder app)
        => app.Use(next =>
        {
            var branch = new ApplicationBuilder(app.ApplicationServices);
            branch.UseRateLimiter();
            branch.Run(next);
            var limited = branch.Build();

            Keep(FindEndpointLimiter(limited.Target, app.ApplicationServices));
            Keep(options.Value.GlobalLimiter);

            return limited;
        });

    /// <summary>Reads the endpoint limiter out of the middleware a pipeline of its own produced.</summary>
    /// <param name="middleware">The target of that pipeline's delegate.</param>
    /// <param name="serviceProvider">The application's services.</param>
    /// <returns>
    /// The endpoint limiter, or <see langword="null"/> when <paramref name="middleware"/> is not the rate
    /// limiting middleware or keeps no limiter in the field this type reads. A warning is logged then,
    /// unless the container owns the middleware and so disposes it itself.
    /// </returns>
    internal PartitionedRateLimiter<HttpContext>? FindEndpointLimiter(object? middleware, IServiceProvider serviceProvider)
    {
        var type = middleware?.GetType();
        var field = string.Equals(type?.FullName, MiddlewareTypeName, StringComparison.Ordinal)
            ? type!.GetField(EndpointLimiterFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            : null;

        if (field?.GetValue(middleware) is PartitionedRateLimiter<HttpContext> limiter)
            return limiter;

        if (!IsMiddlewareOwnedByContainer(serviceProvider))
        {
            logger.LogWarning(
                "The endpoint limiter of ASP.NET Core's rate limiting middleware cannot be released: expected {MiddlewareType} with a limiter in {Field}, found {FoundType}. A disposed host stays reachable through its timer (dotnet/aspnetcore#66434).",
                MiddlewareTypeName,
                EndpointLimiterFieldName,
                type?.FullName ?? "no instance");
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var limiter in Take())
            limiter.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var limiter in Take())
            await limiter.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Whether the container has the middleware registered, and so creates and disposes it.</summary>
    /// <param name="serviceProvider">The application's services.</param>
    /// <returns><see langword="true"/> when the container owns the middleware.</returns>
    private static bool IsMiddlewareOwnedByContainer(IServiceProvider serviceProvider)
    {
        var middlewareType = typeof(RateLimiterOptions).Assembly.GetType(MiddlewareTypeName);

        return middlewareType is not null
            && serviceProvider.GetService<IServiceProviderIsService>()?.IsService(middlewareType) == true;
    }

    /// <summary>Keeps a limiter for disposal, or disposes it at once when this type already was.</summary>
    /// <param name="limiter">The limiter, or <see langword="null"/> when there is none.</param>
    private void Keep(PartitionedRateLimiter<HttpContext>? limiter)
    {
        if (limiter is null)
            return;

        lock (_limiters)
        {
            if (!_isDisposed)
            {
                if (!_limiters.Contains(limiter))
                    _limiters.Add(limiter);

                return;
            }
        }

        limiter.Dispose();
    }

    /// <summary>Marks this type disposed and hands out the limiters it kept, each once.</summary>
    /// <returns>The limiters to dispose.</returns>
    private PartitionedRateLimiter<HttpContext>[] Take()
    {
        lock (_limiters)
        {
            _isDisposed = true;
            PartitionedRateLimiter<HttpContext>[] limiters = [.. _limiters];
            _limiters.Clear();

            return limiters;
        }
    }
}
