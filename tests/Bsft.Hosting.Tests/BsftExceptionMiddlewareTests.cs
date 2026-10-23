using Bsft.Core;
using Bsft.Correlation;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Linq;
using System.Net;
using Xunit;

namespace Bsft.Hosting.Tests;

public class BsftExceptionMiddlewareTests
{
    private static async Task<IHost> CreateHostAsync(
        RequestDelegate handler, Action<IServiceCollection>? configureServices = null)
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
                    configureServices?.Invoke(services);
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

    /// <summary>
    /// A host whose pipeline runs endpoint routing ahead of the middleware, so
    /// <see cref="HttpContext.GetEndpoint"/> (and its metadata) is populated the way bsft-tower's
    /// consumer relies on it being: a <c>CustomizeProblemDetails</c> keyed on endpoint metadata needs the
    /// endpoint to be resolved before the middleware's <see cref="ProblemDetailsContext.HttpContext"/>
    /// is built.
    /// </summary>
    private static async Task<IHost> CreateRoutedHostAsync(
        RequestDelegate handler, object endpointMetadata, Action<IServiceCollection>? configureServices = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddScoped<ICorrelationContext>(_ =>
                    {
                        var ctx = Substitute.For<ICorrelationContext>();
                        ctx.CorrelationId.Returns("test-correlation-id");
                        return ctx;
                    });
                    configureServices?.Invoke(services);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseMiddleware<BsftExceptionMiddleware>();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/", handler).WithMetadata(endpointMetadata);
                    });
                });
            })
            .StartAsync();

        return host;
    }

    private sealed record RouteOwnerMetadata(string Owner);

    /// <summary>
    /// Asserts that a ProblemDetails body carries exactly the fields this middleware itself puts there,
    /// in the order it puts them there, with the values given.
    /// </summary>
    /// <remarks>
    /// Two things vary with the ASP.NET Core version and the JSON options a host configures, neither of
    /// which this middleware controls once it hands the document to <see cref="IProblemDetailsService"/>:
    /// the default writer's <c>JavaScriptEncoder</c> (relaxed, so an apostrophe comes back unescaped
    /// rather than as <c>'</c>) and, since .NET 10, a <c>traceId</c> extension the default writer
    /// appends on its own. Comparing decoded property values rather than raw bytes, and checking that
    /// this middleware's own fields appear as an in-order subsequence rather than demanding an exact key
    /// set, pins the one thing that is actually this middleware's contract — its own fields, values
    /// and their relative order — without the test breaking on a framework-level detail it was never
    /// responsible for.
    /// </remarks>
    private static void AssertProblemDocument(
        string json, int expectedStatus, string expectedType, string expectedTitle, string expectedDetail,
        params (string Key, string Value)[] expectedExtensions)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("type").GetString().Should().Be(expectedType);
        root.GetProperty("title").GetString().Should().Be(expectedTitle);
        root.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        root.GetProperty("detail").GetString().Should().Be(expectedDetail);

        foreach (var (key, value) in expectedExtensions)
            root.GetProperty(key).GetString().Should().Be(value);

        var actualOrder = root.EnumerateObject().Select(p => p.Name).ToList();
        var expectedOrder = new[] { "type", "title", "status", "detail" }
            .Concat(expectedExtensions.Select(e => e.Key))
            .ToList();

        var cursor = 0;
        foreach (var name in expectedOrder)
        {
            var index = actualOrder.IndexOf(name, cursor);
            index.Should().BeGreaterThanOrEqualTo(
                cursor, $"'{name}' should appear at or after position {cursor} in {json}");
            cursor = index + 1;
        }
    }

    private sealed class RecordingProblemDetailsWriter : IProblemDetailsWriter
    {
        public bool WasInvoked { get; private set; }

        public bool CanWrite(ProblemDetailsContext context) => true;

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            WasInvoked = true;
            context.HttpContext.Response.ContentType = "application/problem+json";
            return new ValueTask(context.HttpContext.Response.WriteAsync(
                $"{{\"type\":\"custom-writer\",\"status\":{context.ProblemDetails.Status}}}"));
        }
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

    // --- IProblemDetailsService integration ---
    //
    // The middleware used to serialize mapped exceptions itself, bypassing IProblemDetailsService
    // entirely, so a host's AddProblemDetails(o => o.CustomizeProblemDetails = ...) or a custom
    // IProblemDetailsWriter never saw an exception-mapped body. The tests below pin the default shape
    // (with and without the service registered) and then exercise both customisation seams.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_NotFoundException_KeepsItsFieldsAndOrderRegardlessOfProblemDetailsRegistration(
        bool problemDetailsRegistered)
    {
        using var host = await CreateHostAsync(
            _ => throw new NotFoundException("Order", "order-123"),
            services =>
            {
                if (problemDetailsRegistered)
                    services.AddProblemDetails();
            });

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status404NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var content = await response.Content.ReadAsStringAsync();
        AssertProblemDocument(
            content,
            StatusCodes.Status404NotFound,
            "https://httpstatuses.io/404",
            "Not Found",
            "Entity 'Order' with identifier 'order-123' was not found.",
            ("entityType", "Order"),
            ("entityId", "order-123"),
            ("errorCode", "NOT_FOUND"),
            ("correlationId", "test-correlation-id"));
        await host.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ConflictException_KeepsItsFieldsAndOrderRegardlessOfProblemDetailsRegistration(
        bool problemDetailsRegistered)
    {
        using var host = await CreateHostAsync(
            _ => throw new ConflictException("Already exists"),
            services =>
            {
                if (problemDetailsRegistered)
                    services.AddProblemDetails();
            });

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status409Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var content = await response.Content.ReadAsStringAsync();
        AssertProblemDocument(
            content,
            StatusCodes.Status409Conflict,
            "https://httpstatuses.io/409",
            "Conflict",
            "Already exists",
            ("errorCode", "CONFLICT"),
            ("correlationId", "test-correlation-id"));
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_WithNoProblemDetailsService_BodyIsByteIdentical()
    {
        // The direct-write fallback (no AddProblemDetails registered) is the one path whose body shape
        // this middleware fully controls, so it is the one path worth pinning byte for byte rather than
        // through decoded field comparison.
        using var host = await CreateHostAsync(
            _ => throw new NotFoundException("Order", "order-123"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status404NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be(
            "{\"type\":\"https://httpstatuses.io/404\",\"title\":\"Not Found\",\"status\":404," +
            "\"detail\":\"Entity \\u0027Order\\u0027 with identifier \\u0027order-123\\u0027 was not found.\"," +
            "\"entityType\":\"Order\",\"entityId\":\"order-123\",\"errorCode\":\"NOT_FOUND\"," +
            "\"correlationId\":\"test-correlation-id\"}");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_WithCustomizeProblemDetails_AppliesCustomisation()
    {
        using var host = await CreateHostAsync(
            _ => throw new NotFoundException("Order", "order-123"),
            services => services.AddProblemDetails(options =>
                options.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["nodeId"] = "node-1"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status404NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"nodeId\":\"node-1\"");
        content.Should().Contain("entityType");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ConflictException_WithCustomizeProblemDetails_AppliesCustomisation()
    {
        using var host = await CreateHostAsync(
            _ => throw new ConflictException("Already exists"),
            services => services.AddProblemDetails(options =>
                options.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["nodeId"] = "node-1"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status409Conflict);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"nodeId\":\"node-1\"");
        content.Should().Contain("errorCode");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_MappedException_WithCustomIProblemDetailsWriter_UsesThatWriter()
    {
        var writer = new RecordingProblemDetailsWriter();

        using var host = await CreateHostAsync(
            _ => throw new ConflictException("Already exists"),
            services =>
            {
                services.AddSingleton<IProblemDetailsWriter>(writer);
                services.AddProblemDetails();
            });

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status409Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("{\"type\":\"custom-writer\",\"status\":409}");
        writer.WasInvoked.Should().BeTrue();
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_NoProblemDetailsServiceRegistered_FallsBackToDirectWrite()
    {
        // No AddProblemDetails at all: IProblemDetailsService is not registered, so the middleware must
        // fall back to the write it has always done rather than throw or produce an empty body.
        using var host = await CreateHostAsync(
            _ => throw new NotFoundException("Order", "order-123"));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status404NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"entityType\":\"Order\"");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_ProblemDetailsServiceRegistered_ButNoWriterCanHandleTheRequest_FallsBackToDirectWrite()
    {
        // IProblemDetailsService is registered, but every writer declines (here: the default writer's
        // CanWrite rejects an Accept header it does not support). TryWriteAsync then returns false, and
        // the middleware must still answer with its own direct write rather than an empty 404 body.
        using var host = await CreateHostAsync(
            _ => throw new NotFoundException("Order", "order-123"),
            services => services.AddProblemDetails());

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html"));
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status404NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"entityType\":\"Order\"");
        await host.StopAsync();
    }

    [Fact]
    public async Task InvokeAsync_MappedException_EndpointMetadataIsReachableFromCustomizeProblemDetails()
    {
        // bsft-tower keys its CustomizeProblemDetails on the endpoint's own metadata (replacing a
        // body-rewriting middleware with a customisation that only fires for endpoints carrying a
        // particular marker). The ProblemDetailsContext.HttpContext passed to CustomizeProblemDetails
        // must therefore still carry the endpoint that routing resolved.
        string? observedOwner = null;

        using var host = await CreateRoutedHostAsync(
            _ => throw new ConflictException("Already exists"),
            new RouteOwnerMetadata("legacy-worker-claims"),
            services => services.AddProblemDetails(options =>
                options.CustomizeProblemDetails = ctx =>
                {
                    var metadata = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RouteOwnerMetadata>();
                    observedOwner = metadata?.Owner;
                }));

        var client = host.GetTestClient();
        var response = await client.GetAsync("/");

        ((int)response.StatusCode).Should().Be(StatusCodes.Status409Conflict);
        observedOwner.Should().Be("legacy-worker-claims");
        await host.StopAsync();
    }
}
