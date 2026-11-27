using System.Security.Cryptography;
using System.Text;
using Bsft.Core;
using Bsft.Idempotency.Replay;
using Bsft.Idempotency.Tests.Fakes;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using NSubstitute;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class IdempotencyMiddlewareTests
{
    private static readonly RequestDelegate Next = _ => Task.CompletedTask;

    private static readonly IIdempotencyResponseProtector Protector =
        new IdempotencyResponseProtector(new EphemeralDataProtectionProvider());

    [Fact]
    public void Constructor_NullNext_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyMiddleware(null!, Options.Create(new BsftIdempotencyOptions()), Protector))
            .Should().Throw<ArgumentNullException>().WithParameterName("next");

    [Fact]
    public void Constructor_NullProtector_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyMiddleware(Next, Options.Create(new BsftIdempotencyOptions()), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("protector");

    [Fact]
    public void Constructor_NullOptions_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyMiddleware(Next, null!, Protector))
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
            Options.Create(new BsftIdempotencyOptions()), Protector);

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
        var middleware = new IdempotencyMiddleware(_ => throw failure, Options.Create(new BsftIdempotencyOptions()), Protector);

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
            Options.Create(new BsftIdempotencyOptions()), Protector);

        var invoking = () => middleware.InvokeAsync(context, gate, observer);

        (await invoking.Should().ThrowAsync<ConflictException>())
            .Which.ErrorCode.Should().Be(IdempotencyDefaults.InProgressErrorCode);
        context.Response.Headers.RetryAfter.ToString().Should().Be("7");
        ran.Should().BeFalse();
        gate.Released.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_SensitiveEndpoint_StoresTheBodySealed()
    {
        var context = Protected("key-1", sensitive: true);
        var gate = new StubGate();
        var middleware = new IdempotencyMiddleware(
            async httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                await httpContext.Response.WriteAsync("{\"secret\":\"plaintext-secret\"}");
            },
            Options.Create(new BsftIdempotencyOptions()),
            Protector);

        await middleware.InvokeAsync(context, gate, new RecordingObserver());

        var stored = gate.Completed.Should().ContainSingle().Which.Response;
        Encoding.UTF8.GetString(stored.Body).Should().NotContain("plaintext-secret");
        Protector.FindReplayableBody(stored.Body, isSensitive: true, "scope", "key-1")
            .Should().Equal(Encoding.UTF8.GetBytes("{\"secret\":\"plaintext-secret\"}"));
    }

    [Fact]
    public async Task InvokeAsync_SensitiveEndpointWhoseBodyCannotBeSealed_SendsItAndStoresNothing()
    {
        var dataProtector = Substitute.For<IDataProtector>();
        dataProtector.Protect(Arg.Any<byte[]>()).Returns(_ => throw new CryptographicException("no key"));
        dataProtector.CreateProtector(Arg.Any<string>()).Returns(dataProtector);
        var provider = Substitute.For<IDataProtectionProvider>();
        provider.CreateProtector(IdempotencyResponseProtector.Purpose).Returns(dataProtector);

        var context = Protected("key-1", sensitive: true);
        var gate = new StubGate();
        var middleware = new IdempotencyMiddleware(
            httpContext => httpContext.Response.WriteAsync("{\"secret\":\"x\"}"),
            Options.Create(new BsftIdempotencyOptions()),
            new IdempotencyResponseProtector(provider));

        await middleware.InvokeAsync(context, gate, new RecordingObserver());

        gate.Completed.Should().BeEmpty();
        gate.Released.Should().ContainSingle();
    }

    [Fact]
    public async Task InvokeAsync_SensitiveEndpointReplayOfABodyThatCannotBeUnsealed_RefusesAndTellsTheClientToRotate()
    {
        var ran = false;
        var context = Protected("key-1", sensitive: true);
        var observer = new RecordingObserver();
        var plain = new IdempotencyResponseSnapshot(
            StatusCodes.Status201Created, new Dictionary<string, string>(), "{\"secret\":\"x\"}"u8.ToArray());
        var gate = new StubGate
        {
            Result = new IdempotencyGateResult(
                "scope", "fingerprint", new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed, plain)),
        };
        var middleware = new IdempotencyMiddleware(
            _ =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            Options.Create(new BsftIdempotencyOptions()),
            Protector);

        var invoking = () => middleware.InvokeAsync(context, gate, observer);

        (await invoking.Should().ThrowAsync<ConflictException>())
            .Which.ErrorCode.Should().Be(IdempotencyDefaults.ResponseWithheldErrorCode);
        observer.Actions.Should().Equal(IdempotencyAuditAction.ResponseWithheld);
        context.Response.Body.Length.Should().Be(0);
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_SensitiveEndpointReplay_WritesTheUnsealedBody()
    {
        var context = Protected("key-1", sensitive: true);
        var body = "{\"secret\":\"x\"}"u8.ToArray();
        var stored = new IdempotencyResponseSnapshot(StatusCodes.Status201Created, new Dictionary<string, string>(), Protector.Protect(body, "scope", "key-1")!);
        var gate = new StubGate
        {
            Result = new IdempotencyGateResult(
                "scope", "fingerprint", new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed, stored)),
        };
        var observer = new RecordingObserver();
        context.Response.Body = new MemoryStream();

        await Middleware().InvokeAsync(context, gate, observer);

        ((MemoryStream)context.Response.Body).ToArray().Should().Equal(body);
        observer.Actions.Should().Equal(IdempotencyAuditAction.Replayed);
    }

    [Fact]
    public async Task InvokeAsync_SensitiveEndpointReplayOfABodyCopiedFromAnotherRecord_IsWithheld()
    {
        var context = Protected("key-1", sensitive: true);
        var body = "{\"secret\":\"x\"}"u8.ToArray();
        var copied = new IdempotencyResponseSnapshot(
            StatusCodes.Status201Created, new Dictionary<string, string>(), Protector.Protect(body, "other-scope", "other-key")!);
        var gate = new StubGate
        {
            Result = new IdempotencyGateResult(
                "scope", "fingerprint", new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed, copied)),
        };
        var observer = new RecordingObserver();
        context.Response.Body = new MemoryStream();

        var invoking = () => Middleware().InvokeAsync(context, gate, observer);

        (await invoking.Should().ThrowAsync<ConflictException>())
            .Which.ErrorCode.Should().Be(IdempotencyDefaults.ResponseWithheldErrorCode);
        observer.Actions.Should().Equal(IdempotencyAuditAction.ResponseWithheld);
        context.Response.Body.Length.Should().Be(0);
    }

    private static DefaultHttpContext Protected(string key, bool sensitive = false)
    {
        var context = HttpContexts.Create();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            sensitive
                ? new EndpointMetadataCollection(new IdempotentAttribute(), new SensitiveIdempotentResponseAttribute())
                : new EndpointMetadataCollection(new IdempotentAttribute()),
            "protected"));
        context.Request.Headers["Idempotency-Key"] = key;

        return context;
    }

    private static IdempotencyMiddleware Middleware() =>
        new(Next, Options.Create(new BsftIdempotencyOptions()), Protector);
}
