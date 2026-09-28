using Bsft.Idempotency.Tests.Fakes;
using Bsft.Idempotency.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Bsft.Idempotency.Tests;

/// <summary>
/// The walkthroughs the design is answerable for, driven through a real ASP.NET Core pipeline: an
/// exception middleware, routing, the idempotency middleware and an endpoint that counts how often
/// it actually ran.
/// </summary>
public class IdempotencyAcceptanceTests
{
    [Fact]
    public async Task ADoubleClick_ReplaysTheFirstResponseByteForByteAndRunsTheHandlerOnce()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var first = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-double-click"));
        var second = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-double-click"));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Location!.OriginalString.Should().Be("/orders/1");
        Replayed(first).Should().BeFalse();

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Location!.OriginalString.Should().Be("/orders/1");
        Replayed(second).Should().BeTrue();
        (await second.Content.ReadAsByteArrayAsync())
            .Should().Equal(await first.Content.ReadAsByteArrayAsync());
        second.Content.Headers.ContentType!.ToString().Should().Be("application/json");

        host.Probe.Invocations.Should().Be(1);
        host.Sink.Events.Should().ContainSingle()
            .Which.Action.Should().Be(IdempotencyAuditAction.Replayed);
    }

    [Fact]
    public async Task TwoRequestsStartedAtOnce_LetExactlyOneRunAndTellTheOtherToComeBack()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        host.Probe.Blocking = true;

        var first = host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-race"));

        await host.Probe.Entered;

        var second = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-race"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        second.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        (await ErrorCode(second)).Should().Be(IdempotencyDefaults.InProgressErrorCode);
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        host.Probe.Release();

        (await first).StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.Invocations.Should().Be(1);
        host.Sink.Events.Should().ContainSingle()
            .Which.Action.Should().Be(IdempotencyAuditAction.InProgress);
    }

    [Fact]
    public async Task AKeyPresentedWithADifferentRequest_IsRefusedAndLeavesTheFirstResponseReplayable()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var first = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-reuse", body: "{\"amount\":10}"));
        var reused = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-reuse", body: "{\"amount\":999}"));

        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(reused)).Should().Be(IdempotencyDefaults.KeyReusedErrorCode);
        host.Sink.Events.Should().ContainSingle().Which.Action.Should().Be(IdempotencyAuditAction.KeyReused);

        var replayed = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-reuse", body: "{\"amount\":10}"));

        replayed.StatusCode.Should().Be(HttpStatusCode.Created);
        Replayed(replayed).Should().BeTrue();
        (await replayed.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        host.Probe.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task TheSameKeyUnderAnotherTenant_IsAMissRatherThanAReplay()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var alpha = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-shared", tenant: "alpha"));
        var beta = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-shared", tenant: "beta"));

        alpha.StatusCode.Should().Be(HttpStatusCode.Created);
        beta.StatusCode.Should().Be(HttpStatusCode.Created);
        Replayed(beta).Should().BeFalse();
        (await beta.Content.ReadAsStringAsync()).Should().NotBe(await alpha.Content.ReadAsStringAsync());
        host.Probe.Invocations.Should().Be(2);

        var alphaAgain = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-shared", tenant: "alpha"));

        Replayed(alphaAgain).Should().BeTrue();
        (await alphaAgain.Content.ReadAsStringAsync()).Should().Be(await alpha.Content.ReadAsStringAsync());
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AReservationWhoseLeaseLapsedBecauseTheProcessDied_LetsTheKeyRunAgain()
    {
        await using var host = await IdempotencyTestHost.StartAsync(
            options => options.InProgressTimeout = TimeSpan.FromSeconds(30));

        host.Probe.Blocking = true;

        var abandoned = host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-crash"));

        await host.Probe.Entered;

        host.Probe.Blocking = false;
        host.Clock.Advance(TimeSpan.FromSeconds(31));

        var retried = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-crash"));

        retried.StatusCode.Should().Be(HttpStatusCode.Created);
        Replayed(retried).Should().BeFalse();

        host.Probe.Release();

        (await abandoned).StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AKeyPastItsRetention_RunsAgain()
    {
        await using var host = await IdempotencyTestHost.StartAsync(
            options => options.Retention = TimeSpan.FromMinutes(5));

        await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-expiry"));

        host.Clock.Advance(TimeSpan.FromMinutes(6));

        var afterExpiry = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-expiry"));

        afterExpiry.StatusCode.Should().Be(HttpStatusCode.Created);
        Replayed(afterExpiry).Should().BeFalse();
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AnEndpointThatRequiresAKey_RefusesTheRequestWithoutOneAndSaysWhichHeaderIsMissing()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var response = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders/required"));

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await ErrorCode(response)).Should().Be(IdempotencyDefaults.KeyRequiredErrorCode);
        host.Probe.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task AnEndpointThatDoesNotRequireAKey_RunsUnprotectedWithoutOne()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var first = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders"));
        var second = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders"));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AHostThatRequiresKeysEverywhere_IsOverriddenByAnEndpointThatExemptsItself()
    {
        await using var host = await IdempotencyTestHost.StartAsync(options => options.ShouldRequireKey = true);

        var strict = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders"));
        var exempt = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders/optional"));

        strict.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        exempt.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AWhitespaceKey_CountsAsNoKeyAtAll()
    {
        await using var host = await IdempotencyTestHost.StartAsync(options => options.ShouldRequireKey = true);

        var response = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "   "));

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task TheMiddlewarePlacedInsideTheExceptionMiddleware_NoLongerProducesAProblemDocument()
    {
        await using var wired = await IdempotencyTestHost.StartAsync();
        await using var misordered = await IdempotencyTestHost.StartAsync(exceptionMiddlewareOutside: false);

        var correct = await wired.Client.SendAsync(IdempotencyTestHost.Post("/orders/required"));

        correct.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        correct.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ErrorCode(correct)).Should().Be(IdempotencyDefaults.KeyRequiredErrorCode);

        var wrong = await Attempt(misordered, IdempotencyTestHost.Post("/orders/required"));

        wrong.Should().NotBe(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task AServerErrorIsNotStored_SoTheClientMayRetryIt()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var first = await host.Client.SendAsync(WithStatus("/orders/1/status", "key-500", 500));
        var second = await host.Client.SendAsync(WithStatus("/orders/1/status", "key-500", 500));

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        second.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        Replayed(second).Should().BeFalse();
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AStatusTheHostListedAsNonReplayable_IsNotStoredEither()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        await host.Client.SendAsync(WithStatus("/orders/1/status", "key-429", 429));
        var second = await host.Client.SendAsync(WithStatus("/orders/1/status", "key-429", 429));

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AnOrdinaryClientError_IsReplayedRatherThanRunAgain()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        await host.Client.SendAsync(WithStatus("/orders/1/status", "key-422", 422));
        var second = await host.Client.SendAsync(WithStatus("/orders/1/status", "key-422", 422));

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Replayed(second).Should().BeTrue();
        host.Probe.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task ABodyLargerThanTheFingerprintWillBuffer_IsRefusedRatherThanLetThroughUnprotected()
    {
        await using var host = await IdempotencyTestHost.StartAsync(options => options.MaxBodyBytes = 16);

        var response = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-large", body: new string('x', 512)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ErrorCode(response)).Should().Be(IdempotencyDefaults.PayloadTooLargeErrorCode);
        host.Probe.Invocations.Should().Be(0);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public async Task AMalformedKey_IsAValidationErrorOnTheHeaderRatherThanAFailure(string key)
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        var response = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", key));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("errors")
            .TryGetProperty(IdempotencyDefaults.KeyValidationField, out _).Should().BeTrue();
        host.Probe.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task AnOverLongKey_IsRefusedTheSameWay()
    {
        await using var host = await IdempotencyTestHost.StartAsync(options => options.MaxKeyLength = 8);

        var response = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", new string('a', 9)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Probe.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task AnEndpointNobodyOptedIn_IsUntouchedEvenWithAKey()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        await host.Client.SendAsync(IdempotencyTestHost.Post("/open", "key-open"));
        var second = await host.Client.SendAsync(IdempotencyTestHost.Post("/open", "key-open"));

        Replayed(second).Should().BeFalse();
        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AMethodTheHostDidNotAskToProtect_IsUntouched()
    {
        await using var host = await IdempotencyTestHost.StartAsync();

        using var first = new HttpRequestMessage(HttpMethod.Get, "/orders");
        using var second = new HttpRequestMessage(HttpMethod.Get, "/orders");

        first.Headers.TryAddWithoutValidation(IdempotencyDefaults.HeaderName, "key-get");
        second.Headers.TryAddWithoutValidation(IdempotencyDefaults.HeaderName, "key-get");

        await host.Client.SendAsync(first);
        await host.Client.SendAsync(second);

        host.Probe.Invocations.Should().Be(2);
    }

    [Fact]
    public async Task AHostThatMovedTheHeader_ReadsTheKeyFromWhereItPutIt()
    {
        await using var host = await IdempotencyTestHost.StartAsync(options => options.HeaderName = "X-Request-Id");

        using var first = IdempotencyTestHost.Post("/orders");
        using var second = IdempotencyTestHost.Post("/orders");

        first.Headers.TryAddWithoutValidation("X-Request-Id", "moved");
        second.Headers.TryAddWithoutValidation("X-Request-Id", "moved");

        await host.Client.SendAsync(first);
        var replayed = await host.Client.SendAsync(second);

        Replayed(replayed).Should().BeTrue();
        host.Probe.Invocations.Should().Be(1);
    }

    /// <summary>Sends a request that may not come back as a response at all.</summary>
    /// <param name="host">The host under test.</param>
    /// <param name="request">The request.</param>
    /// <returns>The status answered, or null when the pipeline let the exception escape.</returns>
    private static async Task<HttpStatusCode?> Attempt(IdempotencyTestHost host, HttpRequestMessage request)
    {
        try
        {
            return (await host.Client.SendAsync(request)).StatusCode;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Builds a request asking the status endpoint for a particular status.</summary>
    /// <param name="path">The route.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="status">The status the endpoint answers with.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage WithStatus(string path, string key, int status)
    {
        var request = IdempotencyTestHost.Post(path, key);

        request.Headers.TryAddWithoutValidation(
            IdempotencyTestHost.StatusHeaderName, status.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return request;
    }

    /// <summary>
    /// The ordering rule the topic page calls load-bearing, asserted rather than asserted about: with
    /// authentication in front of the middleware, one key sent by two authenticated callers is two
    /// keys, and neither of them is served the other's response.
    /// </summary>
    [Fact]
    public async Task TwoAuthenticatedCallersSendingOneKey_EachGetTheirOwnResponse()
    {
        await using var host = await IdempotencyTestHost.StartAsync(withAuthentication: true);

        var first = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-shared", subject: "alice"));
        var second = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-shared", subject: "bob"));

        first.Headers.Location!.OriginalString.Should().Be("/orders/1");
        second.Headers.Location!.OriginalString.Should().Be("/orders/2");
        Replayed(second).Should().BeFalse();

        host.Probe.Invocations.Should().Be(2);
    }

    /// <summary>One authenticated caller still gets the replay the key is for.</summary>
    [Fact]
    public async Task OneAuthenticatedCallerSendingAKeyTwice_IsReplayedTo()
    {
        await using var host = await IdempotencyTestHost.StartAsync(withAuthentication: true);

        await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-own", subject: "alice"));
        var second = await host.Client.SendAsync(
            IdempotencyTestHost.Post("/orders", "key-own", subject: "alice"));

        Replayed(second).Should().BeTrue();
        host.Probe.Invocations.Should().Be(1);
    }

    /// <summary>
    /// A store that cannot write the finished record must not cost the client the response it has
    /// already earned. The work the endpoint did is done either way; answering with a server error
    /// would hide a successful outcome behind a failed one and invite a retry of work that ran.
    /// </summary>
    [Fact]
    public async Task WhenTheRecordCannotBeWritten_TheClientStillGetsTheResponseItEarned()
    {
        FailingCompletionStore? store = null;

        await using var host = await IdempotencyTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IIdempotencyStore>(
                _ => store = new FailingCompletionStore(new InMemoryIdempotencyStore(TimeProvider.System))));

        var response = await host.Client.SendAsync(IdempotencyTestHost.Post("/orders", "key-unwritable"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be("/orders/1");
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"id\":1}");

        store!.WasCompletionAttempted.Should().BeTrue();
    }

    /// <summary>Reports whether the response says it is a replay.</summary>
    /// <param name="response">The response.</param>
    /// <returns><see langword="true"/> when the marker header is set.</returns>
    private static bool Replayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(IdempotencyDefaults.ReplayedHeaderName, out var values)
        && values.Contains(IdempotencyDefaults.ReplayedHeaderValue);

    /// <summary>Reads the machine-readable discriminator out of a problem document.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The <c>errorCode</c>.</returns>
    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("errorCode").GetString();
    }
}
