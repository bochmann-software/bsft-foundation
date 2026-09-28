using Bsft.Correlation;
using Bsft.Data.MultiTenancy;
using Bsft.Hosting;
using Bsft.Idempotency.Auditing;
using Bsft.Idempotency.DependencyInjection;
using Bsft.Idempotency.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;

namespace Bsft.Idempotency.Tests.Support;

/// <summary>
/// A real ASP.NET Core pipeline with the idempotency middleware in it, so the acceptance
/// walkthroughs are asserted against a request that actually travels through routing, the exception
/// middleware and an endpoint rather than against a mocked context.
/// </summary>
internal sealed class IdempotencyTestHost : IAsyncDisposable
{
    /// <summary>The header the status-echoing endpoint reads the status it answers with from.</summary>
    public const string StatusHeaderName = "X-Status";

    private readonly IHost _host;

    private IdempotencyTestHost(IHost host, HandlerProbe probe, MutableTimeProvider clock, RecordingAuditSink sink)
    {
        _host = host;
        Probe = probe;
        Clock = clock;
        Sink = sink;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public HandlerProbe Probe { get; }

    public MutableTimeProvider Clock { get; }

    public RecordingAuditSink Sink { get; }

    public IServiceProvider Services => _host.Services;

    /// <summary>Boots the application.</summary>
    /// <param name="configure">What the host decides about idempotency.</param>
    /// <param name="exceptionMiddlewareOutside">
    /// <see langword="false"/> puts <c>BsftExceptionMiddleware</c> inside the idempotency middleware,
    /// which is the ordering mistake the walkthrough exists to make loud.
    /// </param>
    /// <param name="configureServices">Anything else the test needs registered.</param>
    /// <param name="withAuthentication">
    /// <see langword="true"/> puts a real authentication scheme and <c>UseAuthentication()</c> in
    /// front of the idempotency middleware, which is the ordering the topic page calls load-bearing:
    /// the authenticated subject is part of the scope, and a pipeline that authenticates afterwards
    /// puts every caller in one.
    /// </param>
    /// <returns>The started host.</returns>
    public static async Task<IdempotencyTestHost> StartAsync(
        Action<BsftIdempotencyOptions>? configure = null,
        bool exceptionMiddlewareOutside = true,
        Action<IServiceCollection>? configureServices = null,
        bool withAuthentication = false)
    {
        var probe = new HandlerProbe();
        var clock = new MutableTimeProvider();
        var sink = new RecordingAuditSink();

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddSingleton(probe);
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddSingleton<IIdempotencyAuditSink>(sink);
                    services.AddScoped<ITenantContext, HeaderTenantContext>();
                    services.AddScoped<ICorrelationContext, FixedCorrelationContext>();
                    services.AddBsftIdempotency(configure);

                    if (withAuthentication)
                    {
                        services
                            .AddAuthentication(HeaderSubjectAuthenticationHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, HeaderSubjectAuthenticationHandler>(
                                HeaderSubjectAuthenticationHandler.SchemeName, configureOptions: null);
                    }

                    configureServices?.Invoke(services);
                })
                .Configure(app =>
                {
                    if (exceptionMiddlewareOutside)
                        app.UseMiddleware<BsftExceptionMiddleware>();

                    app.UseRouting();

                    if (withAuthentication)
                        app.UseAuthentication();

                    app.UseBsftIdempotency();

                    if (!exceptionMiddlewareOutside)
                        app.UseMiddleware<BsftExceptionMiddleware>();

                    app.UseEndpoints(MapEndpoints);
                }))
            .StartAsync();

        return new IdempotencyTestHost(host, probe, clock, sink);
    }

    /// <summary>Builds a POST the way a client that means to be protected sends one.</summary>
    /// <param name="path">The route.</param>
    /// <param name="key">The idempotency key, or null to send none.</param>
    /// <param name="body">The JSON body.</param>
    /// <param name="tenant">The tenant the caller belongs to, or null for none.</param>
    /// <param name="subject">Who the caller is, for a host that authenticates.</param>
    /// <returns>The request.</returns>
    public static HttpRequestMessage Post(
        string path,
        string? key = null,
        string body = "{\"amount\":10}",
        string? tenant = null,
        string? subject = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        if (key is not null)
            request.Headers.TryAddWithoutValidation(IdempotencyDefaults.HeaderName, key);

        if (tenant is not null)
            request.Headers.TryAddWithoutValidation(HeaderTenantContext.HeaderName, tenant);

        if (subject is not null)
            request.Headers.TryAddWithoutValidation(HeaderSubjectAuthenticationHandler.HeaderName, subject);

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        await _host.StopAsync();

        _host.Dispose();
    }

    /// <summary>Maps the routes the walkthroughs drive.</summary>
    /// <param name="endpoints">The route builder.</param>
    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/orders", CreateAsync).RequireIdempotency();
        endpoints.MapPost("/orders/{id}/status", StatusAsync).RequireIdempotency();
        endpoints.MapPost("/orders/required", CreateAsync).RequireIdempotencyKey();
        endpoints.MapPost("/orders/optional", CreateAsync)
            .WithMetadata(new IdempotentAttribute(requireKey: false));
        endpoints.MapPost("/open", CreateAsync);
        endpoints.MapGet("/orders", CreateAsync).RequireIdempotency();
    }

    /// <summary>Answers 201 with a <c>Location</c> naming what it just created.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    private static async Task CreateAsync(HttpContext context)
    {
        var invocation = await Invoke(context);

        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.Location = "/orders/" + invocation.ToString(CultureInfo.InvariantCulture);
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(Body(invocation));
    }

    /// <summary>Answers with whatever status the caller asked it to, so a non-replayable one can be produced.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    private static async Task StatusAsync(HttpContext context)
    {
        var invocation = await Invoke(context);
        var requested = context.Request.Headers[StatusHeaderName].ToString();

        context.Response.StatusCode = string.IsNullOrEmpty(requested)
            ? StatusCodes.Status200OK
            : int.Parse(requested, CultureInfo.InvariantCulture);
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(Body(invocation));
    }

    /// <summary>Counts the invocation, and holds it when the test asked for a standing reservation.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The number of this invocation.</returns>
    private static Task<int> Invoke(HttpContext context) =>
        context.RequestServices.GetRequiredService<HandlerProbe>().EnterAsync();

    /// <summary>The body, which names the invocation so a replay is distinguishable from a re-run.</summary>
    /// <param name="invocation">The invocation number.</param>
    /// <returns>The JSON body.</returns>
    private static string Body(int invocation) =>
        "{\"id\":" + invocation.ToString(CultureInfo.InvariantCulture) + "}";
}
