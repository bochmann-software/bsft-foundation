using Bsft.Core;
using Bsft.Idempotency.Replay;
using Bsft.Idempotency.Tests.Fakes;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class IdempotencyMiddlewareTests
{
    private static readonly RequestDelegate Next = _ => Task.CompletedTask;

    [Fact]
    public void Constructor_NullNext_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyMiddleware(null!, Options.Create(new BsftIdempotencyOptions())))
            .Should().Throw<ArgumentNullException>().WithParameterName("next");

    [Fact]
    public void Constructor_NullOptions_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyMiddleware(Next, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");

    [Fact]
    public async Task InvokeAsync_NullContext_IsRefused() =>
        await FluentActions
            .Awaiting(() => Middleware().InvokeAsync(null!, new StubGate(), new RecordingObserver()))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");

    [Fact]
    public async Task InvokeAsync_NullGate_IsRefused() =>
        await FluentActions
            .Awaiting(() => Middleware().InvokeAsync(HttpContexts.Create(), null!, new RecordingObserver()))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("gate");

    [Fact]
    public async Task InvokeAsync_NullObserver_IsRefused() =>
        await FluentActions
            .Awaiting(() => Middleware().InvokeAsync(HttpContexts.Create(), new StubGate(), null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("observer");

    [Fact]
    public async Task InvokeAsync_AnEndpointNobodyOptedIn_IsPassedStraightOn()
    {
        var ran = false;
        var middleware = new IdempotencyMiddleware(
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            Options.Create(new BsftIdempotencyOptions()));

        await middleware.InvokeAsync(HttpContexts.Create(), new StubGate(), new RecordingObserver());

        ran.Should().BeTrue();
    }

    /// <summary>
    /// The store is a public seam, and its contract says a replay carries the stored response. A host's
    /// own implementation that answers Replayed with nothing is a contract breach, and it is named as
    /// one rather than surfacing as a NullReferenceException thrown out of the middleware.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenAStoreReplaysWithNoStoredResponse_SaysSo()
    {
        var context = HttpContexts.Create();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotentAttribute()),
            "replaying"));
        context.Request.Headers["Idempotency-Key"] = "key-1";

        var gate = new StubGate
        {
            Result = new IdempotencyGateResult(
                "scope", "fingerprint", new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed))
        };

        var invoking = () => Middleware().InvokeAsync(context, gate, new RecordingObserver());

        await invoking.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*carried no stored response*");
    }

    /// <summary>
    /// A request that threw earned no response, so its reservation is dropped rather than stored: the
    /// client that retries it must be allowed to run it again, and the failure still reaches the host.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTheEndpointThrows_ReleasesTheReservationAndRethrows()
    {
        var failure = new InvalidOperationException("the endpoint failed");
        var context = Protected("key-1");
        var gate = new StubGate();
        var middleware = new IdempotencyMiddleware(_ => throw failure, Options.Create(new BsftIdempotencyOptions()));

        var invoking = () => middleware.InvokeAsync(context, gate, new RecordingObserver());

        (await invoking.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        gate.Released.Should().ContainSingle().Which.Should().Be(("scope", "key-1", "fingerprint"));
        gate.Completed.Should().BeEmpty();
    }

    /// <summary>
    /// A key still held by a request that has not finished is refused with a conflict that says when to
    /// come back, and the endpoint does not run a second time meanwhile.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTheKeyIsStillRunning_RefusesWithARetryAfterAndDoesNotRun()
    {
        var ran = false;
        var context = Protected("key-1");
        var observer = new RecordingObserver();
        var gate = new StubGate
        {
            RetryAfter = 7,
            Result = new IdempotencyGateResult(
                "scope", "fingerprint", new IdempotencyBeginResult(IdempotencyBeginOutcome.InProgress)),
        };
        var middleware = new IdempotencyMiddleware(
            _ =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            Options.Create(new BsftIdempotencyOptions()));

        var invoking = () => middleware.InvokeAsync(context, gate, observer);

        (await invoking.Should().ThrowAsync<ConflictException>())
            .Which.ErrorCode.Should().Be(IdempotencyDefaults.InProgressErrorCode);
        context.Response.Headers.RetryAfter.ToString().Should().Be("7");
        ran.Should().BeFalse();
        gate.Released.Should().BeEmpty();
    }

    private static DefaultHttpContext Protected(string key)
    {
        var context = HttpContexts.Create();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotentAttribute()),
            "protected"));
        context.Request.Headers["Idempotency-Key"] = key;

        return context;
    }

    private static IdempotencyMiddleware Middleware() =>
        new(Next, Options.Create(new BsftIdempotencyOptions()));
}
