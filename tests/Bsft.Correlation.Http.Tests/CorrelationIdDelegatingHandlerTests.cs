using FluentAssertions;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Correlation.Http.Tests;

/// <summary>
/// What the outbound handler does to a request on its way out.
/// </summary>
public sealed class CorrelationIdDelegatingHandlerTests
{
    [Fact]
    public async Task SendAsync_AddsCorrelationIdHeader_ToOutgoingRequests()
    {
        var inner = new RecordingHandler();
        var handler = new CorrelationIdDelegatingHandler(ContextWith("outgoing-id"))
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(new Uri("http://localhost/test"));

        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Headers.GetValues(CorrelationDefaults.CorrelationId)
            .Should().ContainSingle().Which.Should().Be("outgoing-id");
    }

    [Fact]
    public async Task SendAsync_DoesNotOverwrite_ExistingHeader()
    {
        var inner = new RecordingHandler();
        var handler = new CorrelationIdDelegatingHandler(ContextWith("new-id"))
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost/test"));
        request.Headers.Add(CorrelationDefaults.CorrelationId, "existing-id");

        using var response = await client.SendAsync(request);

        inner.LastRequest!.Headers.GetValues(CorrelationDefaults.CorrelationId)
            .Should().ContainSingle().Which.Should().Be("existing-id");
    }

    [Fact]
    public void Constructor_WithNullCorrelationContext_ThrowsArgumentNullException()
    {
        var act = () => new CorrelationIdDelegatingHandler(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationContext");
    }

    [Fact]
    public void SendAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Exercises the null-request guard. The method is protected and the handler is sealed, so it is
        // invoked reflectively rather than through a subclass; nothing in the product reaches it, which
        // is exactly why the guard needs a test.
        var handler = new CorrelationIdDelegatingHandler(ContextWith("test-id"))
        {
            InnerHandler = new RecordingHandler()
        };

        var sendAsync = typeof(CorrelationIdDelegatingHandler)
            .GetMethod("SendAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var act = () => sendAsync.Invoke(handler, [null!, CancellationToken.None]);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .WithParameterName("request");
    }

    private static ICorrelationContext ContextWith(string correlationId)
    {
        var context = Substitute.For<ICorrelationContext>();
        context.CorrelationId.Returns(correlationId);

        return context;
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        // Instance state (not static) so parallel tests using their own handler cannot race on it.
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
