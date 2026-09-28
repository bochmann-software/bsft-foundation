using Bsft.Idempotency.Replay;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using System.Text;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class IdempotencyReplayWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesTheStoredStatusHeadersAndBytesAndMarksTheAnswerAsAReplay()
    {
        var context = HttpContexts.Create();
        var body = Encoding.UTF8.GetBytes("{\"id\":1}");
        var snapshot = new IdempotencyResponseSnapshot(
            201,
            new Dictionary<string, string> { ["Location"] = "/orders/1", ["Content-Type"] = "application/json" },
            body);

        await IdempotencyReplayWriter.WriteAsync(context.Response, snapshot);

        context.Response.StatusCode.Should().Be(201);
        context.Response.Headers.Location.ToString().Should().Be("/orders/1");
        context.Response.ContentType.Should().Be("application/json");
        context.Response.Headers[IdempotencyDefaults.ReplayedHeaderName].ToString()
            .Should().Be(IdempotencyDefaults.ReplayedHeaderValue);
        context.Response.ContentLength.Should().Be(body.Length);
        ((MemoryStream)context.Response.Body).ToArray().Should().Equal(body);
    }

    [Fact]
    public async Task WriteAsync_ASnapshotWithNoHeadersAtAll_StillMarksTheReplay()
    {
        var context = HttpContexts.Create();

        await IdempotencyReplayWriter.WriteAsync(
            context.Response, new IdempotencyResponseSnapshot(204, new Dictionary<string, string>(), []));

        context.Response.StatusCode.Should().Be(204);
        context.Response.ContentLength.Should().Be(0);
        context.Response.Headers[IdempotencyDefaults.ReplayedHeaderName].ToString()
            .Should().Be(IdempotencyDefaults.ReplayedHeaderValue);
    }

    [Fact]
    public async Task WriteAsync_NullResponse_IsRefused() =>
        await FluentActions
            .Awaiting(() => IdempotencyReplayWriter.WriteAsync(
                null!, new IdempotencyResponseSnapshot(200, new Dictionary<string, string>(), [])))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("response");

    [Fact]
    public async Task WriteAsync_NullSnapshot_IsRefused() =>
        await FluentActions
            .Awaiting(() => IdempotencyReplayWriter.WriteAsync(HttpContexts.Create().Response, null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("snapshot");
}
