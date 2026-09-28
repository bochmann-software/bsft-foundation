using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace Bsft.Idempotency.Tests.Support;

/// <summary>Builds the request shapes the unit tests drive, without a pipeline behind them.</summary>
internal static class HttpContexts
{
    /// <summary>Builds a request with a body, a route and whatever else the test dictated.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The raw path.</param>
    /// <param name="body">The body bytes, as UTF-8 text.</param>
    /// <param name="contentType">The declared content type.</param>
    /// <param name="query">The query string, starting with <c>?</c>.</param>
    /// <param name="routePattern">The pattern of the matched route, or null for no matched endpoint.</param>
    /// <param name="services">The request services the fingerprinter reads the tenant from.</param>
    /// <param name="user">The caller.</param>
    /// <param name="remoteAddress">The connection's remote address, or null for none.</param>
    /// <returns>The context.</returns>
    public static DefaultHttpContext Create(
        string method = "POST",
        string path = "/api/orders",
        string body = "{}",
        string? contentType = "application/json",
        string? query = null,
        string? routePattern = null,
        IServiceProvider? services = null,
        ClaimsPrincipal? user = null,
        string? remoteAddress = null)
    {
        var context = new DefaultHttpContext();

        Fill(context, method, path, body, contentType, query, services, user);

        if (remoteAddress is not null)
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        if (routePattern is not null)
            context.SetEndpoint(RouteEndpointFor(routePattern));

        return context;
    }

    /// <summary>Builds a context whose <see cref="HttpContext.User"/> is genuinely absent.</summary>
    /// <param name="services">The request services.</param>
    /// <param name="remoteAddress">The connection's remote address, or null for none.</param>
    /// <returns>The context.</returns>
    /// <remarks>
    /// <see cref="DefaultHttpContext"/> manufactures an empty principal rather than answering null,
    /// so the "nobody is signed in at all" case needs a context that says so.
    /// </remarks>
    public static HttpContext CreateWithoutUser(
        IServiceProvider? services = null, string? remoteAddress = null)
    {
        var inner = new DefaultHttpContext();

        Fill(inner, "POST", "/api/orders", "{}", "application/json", query: null, services, user: null);

        if (remoteAddress is not null)
            inner.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        return new UserlessHttpContext(inner);
    }

    /// <summary>Builds an endpoint that is not a <see cref="RouteEndpoint"/>.</summary>
    /// <returns>The endpoint.</returns>
    public static Endpoint PlainEndpoint() =>
        new(_ => Task.CompletedTask, new EndpointMetadataCollection(), "plain");

    /// <summary>Builds a route endpoint for a pattern.</summary>
    /// <param name="pattern">The route pattern.</param>
    /// <returns>The endpoint.</returns>
    public static RouteEndpoint RouteEndpointFor(string pattern) =>
        new(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            new EndpointMetadataCollection(),
            pattern);

    /// <summary>Puts the request together.</summary>
    /// <param name="context">The context being filled.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The raw path.</param>
    /// <param name="body">The body bytes, as UTF-8 text.</param>
    /// <param name="contentType">The declared content type.</param>
    /// <param name="query">The query string.</param>
    /// <param name="services">The request services.</param>
    /// <param name="user">The caller.</param>
    private static void Fill(
        DefaultHttpContext context,
        string method,
        string path,
        string body,
        string? contentType,
        string? query,
        IServiceProvider? services,
        ClaimsPrincipal? user)
    {
        var bytes = Encoding.UTF8.GetBytes(body);

        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Response.Body = new MemoryStream();

        if (query is not null)
            context.Request.QueryString = new QueryString(query);

        if (services is not null)
            context.RequestServices = services;

        if (user is not null)
            context.User = user;
    }

    /// <summary>
    /// A context with no caller at all, rather than an empty one. Everything but the caller is the
    /// inner context's, because <see cref="DefaultHttpContext"/> is sealed and always manufactures a
    /// principal, so the "nobody is signed in" case has no other way to exist.
    /// </summary>
    private sealed class UserlessHttpContext : HttpContext
    {
        private readonly DefaultHttpContext _inner;

        public UserlessHttpContext(DefaultHttpContext inner) => _inner = inner;

        public override IFeatureCollection Features => _inner.Features;

        public override HttpRequest Request => _inner.Request;

        public override HttpResponse Response => _inner.Response;

        public override ConnectionInfo Connection => _inner.Connection;

        public override WebSocketManager WebSockets => _inner.WebSockets;

        public override ClaimsPrincipal User
        {
            get => null!;
            set { }
        }

        public override IDictionary<object, object?> Items
        {
            get => _inner.Items;
            set => _inner.Items = value;
        }

        public override IServiceProvider RequestServices
        {
            get => _inner.RequestServices;
            set => _inner.RequestServices = value;
        }

        public override CancellationToken RequestAborted
        {
            get => _inner.RequestAborted;
            set => _inner.RequestAborted = value;
        }

        public override string TraceIdentifier
        {
            get => _inner.TraceIdentifier;
            set => _inner.TraceIdentifier = value;
        }

        public override ISession Session
        {
            get => _inner.Session;
            set => _inner.Session = value;
        }

        public override void Abort() => _inner.Abort();
    }
}
