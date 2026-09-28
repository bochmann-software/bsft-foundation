namespace Bsft.Correlation.Http;

/// <summary>
/// A delegating handler that attaches the <c>X-Correlation-Id</c> header to outgoing HTTP requests.
/// Register this handler on an <c>IHttpClientBuilder</c> (via <c>AddHttpMessageHandler</c>) to
/// propagate correlation IDs across service boundaries.
/// </summary>
/// <remarks>
/// It attaches the header to every request the client it is registered on sends, including one that
/// follows a redirect to another host, and it knows nothing about where that is. The identifier is
/// not a credential, but it does describe how a deployment traces itself, so the client to put this
/// handler on is one that talks inside the trust boundary. A client calling a third party is left
/// without it, which is a decision a host makes per registration rather than one this handler can
/// make for it.
/// </remarks>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly ICorrelationContext _correlationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdDelegatingHandler"/> class.
    /// </summary>
    /// <param name="correlationContext">The correlation context providing the current correlation ID.</param>
    public CorrelationIdDelegatingHandler(ICorrelationContext correlationContext)
    {
        _correlationContext = correlationContext ?? throw new ArgumentNullException(nameof(correlationContext));
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!request.Headers.Contains(CorrelationDefaults.CorrelationId))
        {
            request.Headers.Add(CorrelationDefaults.CorrelationId, _correlationContext.CorrelationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
