using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class EgressGuardHandlerTests
{
    [Fact]
    public void Constructor_WithoutAPolicy_Throws()
    {
        var act = () => new EgressGuardHandler(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_AnAllowedUrl_ReachesTheInnerHandler()
    {
        var inner = new RecordingHandler();
        using var client = Client(Policy(), inner);

        using var response = await client.GetAsync(new Uri("https://hooks.example.com/deliver"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ARefusedUrl_NeverReachesTheInnerHandler()
    {
        var inner = new RecordingHandler();
        using var client = Client(Policy(), inner);

        var act = () => client.GetAsync(new Uri("http://169.254.169.254/latest/meta-data/"));

        // Wrapped the way the connect-time half of the same defence is wrapped, so a caller that
        // treats a blocked destination as an unreachable one catches one exception type, not two.
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithInnerException<HttpRequestException, OutboundAddressRejectedException>();
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_AHostsOwnPolicyThatRefuses_IsObeyed()
    {
        // The policy is a seam, and a host that replaces it decides what this handler refuses.
        var policy = Substitute.For<IOutboundAddressPolicy>();
        policy.Evaluate(Arg.Any<Uri?>()).Returns(OutboundAddressDecision.Denied("not on the corporate list"));

        var inner = new RecordingHandler();
        using var client = Client(policy, inner);

        var act = () => client.GetAsync(new Uri("https://hooks.example.com/deliver"));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.InnerException.Should().BeOfType<OutboundAddressRejectedException>()
            .Which.Message.Should().Be("not on the corporate list");
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WithoutARequest_Throws()
    {
        var handler = new EgressGuardHandler(Policy()) { InnerHandler = new RecordingHandler() };
        using var invoker = new HttpMessageInvoker(handler);

        var act = () => invoker.SendAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static HttpClient Client(IOutboundAddressPolicy policy, HttpMessageHandler inner) =>
        new(new EgressGuardHandler(policy) { InnerHandler = inner });

    private static IOutboundAddressPolicy Policy() =>
        new OutboundAddressPolicy(Microsoft.Extensions.Options.Options.Create(new BsftHttpEgressOptions()));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
