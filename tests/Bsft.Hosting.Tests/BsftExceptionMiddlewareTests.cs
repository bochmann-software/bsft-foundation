using Bsft.Core;
using Bsft.Correlation;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using Xunit;

namespace Bsft.Hosting.Tests;

public class BsftExceptionMiddlewareTests
{
    private static async Task<IHost> CreateHostAsync(RequestDelegate handler)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddScoped<ICorrelationContext>(_ =>
                    {
                        var ctx = Substitute.For<ICorrelationContext>();
                        ctx.CorrelationId.Returns("test-correlation-id");
                        return ctx;
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<BsftExceptionMiddleware>();
                    app.Run(handler);
                });
            })
            .StartAsync();

        return host;
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        using var host = await CreateHostAsync(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_Returns400()
    {
        using var host = await CreateHostAsync(_ =>
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Name"] = ["Name is required"]
            }));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Validation Error");
        content.Should().Contain("errors");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404()
    {
        using var host = await CreateHostAsync(_ =>
            throw new NotFoundException("Order", "order-123"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Not Found");
        content.Should().Contain("entityType");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ForbiddenException_Returns403()
    {
        using var host = await CreateHostAsync(_ =>
            throw new ForbiddenException("Access denied"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Forbidden");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ConflictException_Returns409()
    {
        using var host = await CreateHostAsync(_ =>
            throw new ConflictException("Already exists"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Conflict");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ConflictException_SurfacesErrorCode()
    {
        // A client must be able to tell two 409s apart in code, not only by their prose. The
        // discriminator is the exception's ErrorCode, written into the problem document as `errorCode`.
        using var host = await CreateHostAsync(_ =>
            throw new ConflictException("Already exists"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("errorCode");
        content.Should().Contain("CONFLICT");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_BusinessRuleException_Returns422()
    {
        using var host = await CreateHostAsync(_ =>
            throw new BusinessRuleException("Cannot process order"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Business Rule Violation");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_BusinessRuleException_SurfacesCustomErrorCode()
    {
        // The whole point of the code on a 422: two business-rule violations at the same status are
        // distinguished by the custom code the handler chose, here PRODUCT_HAS_STOCK.
        using var host = await CreateHostAsync(_ =>
            throw new BusinessRuleException("Still holds stock", "PRODUCT_HAS_STOCK"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("errorCode");
        content.Should().Contain("PRODUCT_HAS_STOCK");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_BusinessRuleExceptionWithADeadline_SurfacesRetryAfter()
    {
        // A refusal that lifts at a known instant says so in the document, so a client can act on it
        // rather than read it out of the prose.
        using var host = await CreateHostAsync(_ =>
            throw new BusinessRuleException(
                "The account is locked out.",
                "CREDENTIAL_LOCKED_OUT",
                new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("retryAfter");
        content.Should().Contain("2026-08-18T12:00:00");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_BusinessRuleExceptionWithoutADeadline_OmitsRetryAfter()
    {
        using var host = await CreateHostAsync(_ =>
            throw new BusinessRuleException("Still holds stock", "PRODUCT_HAS_STOCK"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("retryAfter");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_PreconditionRequiredException_Returns428WithErrorCode()
    {
        // A conditional write made without an If-Match: the framework's own exception, mapped to 428
        // so the client knows to read the resource for its version and retry.
        using var host = await CreateHostAsync(_ =>
            throw new PreconditionRequiredException("If-Match is required"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status428PreconditionRequired);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Precondition Required");
        content.Should().Contain("errorCode");
        content.Should().Contain("PRECONDITION_REQUIRED");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_PayloadTooLargeException_Returns413WithErrorCode()
    {
        // A body larger than the bound a feature is willing to read it under: refused loudly, because
        // the alternative is proceeding without the guarantee the body would have bought.
        using var host = await CreateHostAsync(_ =>
            throw new PayloadTooLargeException("The request body is too large to fingerprint."));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status413PayloadTooLarge);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Payload Too Large");
        content.Should().Contain("errorCode");
        content.Should().Contain("PAYLOAD_TOO_LARGE");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_PaymentRequiredException_Returns402WithErrorCode()
    {
        // A request that is well formed and permitted, and fails only because the plan does not carry
        // the entitlement it needs. 402 rather than 422, because the same request succeeds on a larger
        // plan: the refusal is about the plan, not about the resource's own state.
        using var host = await CreateHostAsync(_ =>
            throw new PaymentRequiredException(
                "The quota for 'api.calls' is exhausted until 2026-09-01.", "QUOTA_EXCEEDED"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status402PaymentRequired);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Payment Required");
        content.Should().Contain("errorCode");
        content.Should().Contain("QUOTA_EXCEEDED");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ConflictExceptionWithItsOwnErrorCode_PublishesThatCode()
    {
        // A family with more than one conflict at 409 names each of them, so a client branches on the
        // code rather than on the wording of the detail.
        using var host = await CreateHostAsync(_ =>
            throw new ConflictException("The key is in flight.", "IDEMPOTENCY_IN_PROGRESS"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status409Conflict);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("IDEMPOTENCY_IN_PROGRESS");
        content.Should().NotContain("\"errorCode\":\"CONFLICT\"");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ConflictExceptionNamingWhatItCollidedWith_PublishesThoseMembers()
    {
        // The code says which kind of conflict it is; it does not say which resource. A thrower that
        // knows the identity states it, so the client acts on it without parsing the detail.
        using var host = await CreateHostAsync(_ => throw new ConflictException(
            "A legal hold covers it.", "CHAT_COMPLIANCE_HOLD", "legalHold", "hold-7"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status409Conflict);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"resourceType\":\"legalHold\"");
        content.Should().Contain("\"resourceId\":\"hold-7\"");
        content.Should().Contain("CHAT_COMPLIANCE_HOLD");
        content.Should().Contain("correlationId");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ConflictExceptionNamingOnlyTheResourceKind_OmitsTheAbsentMember()
    {
        // A member that is present but null reads to a client exactly like a member it has to handle;
        // the one the thrower did not state is left out of the document instead.
        using var host = await CreateHostAsync(_ => throw new ConflictException(
            "A legal hold covers it.", "CHAT_COMPLIANCE_HOLD", "legalHold", null));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"resourceType\":\"legalHold\"");
        content.Should().NotContain("resourceId");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_PreconditionFailedException_Returns412WithErrorCode()
    {
        // A stale If-Match: the framework's translation of an optimistic-concurrency conflict, mapped
        // to 412 rather than surfacing a provider exception as a 500.
        using var host = await CreateHostAsync(_ =>
            throw new PreconditionFailedException("The product has changed"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status412PreconditionFailed);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Precondition Failed");
        content.Should().Contain("errorCode");
        content.Should().Contain("PRECONDITION_FAILED");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_GenericBsftException_Returns500()
    {
        using var host = await CreateHostAsync(_ =>
            throw new BsftException("Generic framework error", "INTERNAL"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Internal Server Error");
        // The unmapped framework exception's message must not leak to the client.
        content.Should().NotContain("Generic framework error");
        // Nor its code: the 500 arm is deliberately opaque but for the correlation id, so an internal
        // error taxonomy is not published to the client.
        content.Should().NotContain("errorCode");
        content.Should().NotContain("INTERNAL");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ResponseAlreadyStarted_DoesNotThrow()
    {
        using var host = await CreateHostAsync(async ctx =>
        {
            await ctx.Response.WriteAsync("partial");
            await ctx.Response.Body.FlushAsync();
            throw new InvalidOperationException("boom after response started");
        });

        var client = host.GetTestClient();
        // The server must not crash trying to set the status after the response has started.
        var response = await client.GetAsync("/");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("partial");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceledOnAbortedRequest_Rethrows()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        var correlationContext = Substitute.For<ICorrelationContext>();
        RequestDelegate next = _ => throw new OperationCanceledException(cts.Token);
        var middleware = new BsftExceptionMiddleware(next, Substitute.For<ILogger<BsftExceptionMiddleware>>());

        var act = async () => await middleware.InvokeAsync(context, correlationContext);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceledWithoutAbort_IsHandledAs500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var correlationContext = Substitute.For<ICorrelationContext>();
        correlationContext.CorrelationId.Returns("cid");
        RequestDelegate next = _ => throw new OperationCanceledException();
        var middleware = new BsftExceptionMiddleware(next, Substitute.For<ILogger<BsftExceptionMiddleware>>());

        await middleware.InvokeAsync(context, correlationContext);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_Returns500WithCorrelationId()
    {
        using var host = await CreateHostAsync(_ =>
            throw new InvalidOperationException("Something went wrong"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("correlationId");
        content.Should().Contain("test-correlation-id");
        await host.StopAsync();
    }

    [Fact]
    public void Constructor_NullNext_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<BsftExceptionMiddleware>>();

        var act = () => new BsftExceptionMiddleware(null!, logger);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("next");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var act = () => new BsftExceptionMiddleware(next, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task InvokeAsync_NullCorrelationContext_ThrowsArgumentNullException()
    {
        // Exercises BsftExceptionMiddleware.InvokeAsync line 45: null correlationContext guard.
        var logger = Substitute.For<ILogger<BsftExceptionMiddleware>>();
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new BsftExceptionMiddleware(next, logger);

        var context = new DefaultHttpContext();

        var act = async () => await middleware.InvokeAsync(context, null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("correlationContext");
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        // Exercises BsftExceptionMiddleware.InvokeAsync line 42-43: null context guard.
        var logger = Substitute.For<ILogger<BsftExceptionMiddleware>>();
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new BsftExceptionMiddleware(next, logger);
        var correlationContext = Substitute.For<ICorrelationContext>();

        var act = async () => await middleware.InvokeAsync(null!, correlationContext);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("context");
    }
}
