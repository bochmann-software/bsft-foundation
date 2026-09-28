namespace Bsft.Hosting.Tests.Fakes;

/// <summary>An HTTP handler that fails every request with the exception it was given.</summary>
public sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _thrown;

    public ThrowingHttpMessageHandler(Exception thrown)
    {
        _thrown = thrown ?? throw new ArgumentNullException(nameof(thrown));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => throw _thrown;
}
