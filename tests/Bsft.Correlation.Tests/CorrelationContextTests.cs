using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Bsft.Correlation.Tests;

public class CorrelationContextTests
{
    private readonly ILogger<CorrelationIdMiddleware> _logger = Substitute.For<ILogger<CorrelationIdMiddleware>>();

    [Fact]
    public void CorrelationContext_HasDefaultGuidValue()
    {
        var context = new CorrelationContext();

        context.CorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(context.CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public void CorrelationContext_AllowsSettingValue()
    {
        var context = new CorrelationContext();
        context.CorrelationId = "custom-id";

        context.CorrelationId.Should().Be("custom-id");
    }

    [Fact]
    public async Task Middleware_GeneratesNewId_WhenHeaderNotPresent()
    {
        var correlationContext = new CorrelationContext();
        var httpContext = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, correlationContext, _logger);

        correlationContext.CorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(correlationContext.CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_UsesExistingHeader_WhenPresent()
    {
        var correlationContext = new CorrelationContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "incoming-id";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, correlationContext, _logger);

        correlationContext.CorrelationId.Should().Be("incoming-id");
    }

    [Fact]
    public async Task Middleware_RejectsHeaderWithCrlf_GeneratesSafeId()
    {
        var correlationContext = new CorrelationContext();
        var httpContext = new DefaultHttpContext();
        // CRLF injection payload attempting to forge a second log line / response header.
        httpContext.Request.Headers["X-Correlation-Id"] = "legit\r\nInjected: evil";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, correlationContext, _logger);

        correlationContext.CorrelationId.Should().NotContain("\n").And.NotContain("\r");
        Guid.TryParse(correlationContext.CorrelationId, out _).Should().BeTrue();
        httpContext.Response.Headers["X-Correlation-Id"].ToString().Should().Be(correlationContext.CorrelationId);
    }

    [Fact]
    public async Task Middleware_RejectsOversizedHeader_GeneratesSafeId()
    {
        var correlationContext = new CorrelationContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = new string('a', 129);
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, correlationContext, _logger);

        Guid.TryParse(correlationContext.CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_AddsCorrelationIdToResponseHeaders()
    {
        var correlationContext = new CorrelationContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "response-test-id";

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, correlationContext, _logger);

        httpContext.Response.Headers["X-Correlation-Id"].ToString().Should().Be("response-test-id");
    }

    [Fact]
    public async Task Middleware_PushesCorrelationIdIntoLoggerScope()
    {
        var correlationContext = new CorrelationContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "scope-test-id";

        Dictionary<string, object>? capturedScope = null;
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        _logger.BeginScope(Arg.Do<Dictionary<string, object>>(s => capturedScope = s))
            .Returns(Substitute.For<IDisposable>());

        await middleware.InvokeAsync(httpContext, correlationContext, _logger);

        capturedScope.Should().NotBeNull();
        capturedScope.Should().ContainKey("CorrelationId").WhoseValue.Should().Be("scope-test-id");
    }

    [Fact]
    public async Task Middleware_WithNullCorrelationContext_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdMiddleware.InvokeAsync line 39: null correlationContext guard.
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();

        var act = async () => await middleware.InvokeAsync(httpContext, null!, _logger);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("correlationContext");
    }

    [Fact]
    public async Task Middleware_WithNullLogger_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdMiddleware.InvokeAsync line 41: null logger guard.
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        var correlationContext = new CorrelationContext();

        var act = async () => await middleware.InvokeAsync(httpContext, correlationContext, null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task Middleware_WithNullContext_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdMiddleware.InvokeAsync line 37: null context guard.
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        var correlationContext = new CorrelationContext();

        var act = async () => await middleware.InvokeAsync(null!, correlationContext, _logger);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("context");
    }

    [Fact]
    public void LoggingEnricher_Constructor_NullNext_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdLoggingEnricher.ctor line 20: null next guard.
        var act = () => new CorrelationIdLoggingEnricher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("next");
    }

    [Fact]
    public async Task LoggingEnricher_InvokeAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdLoggingEnricher.InvokeAsync line 32: null context guard.
        var enricher = new CorrelationIdLoggingEnricher(_ => Task.CompletedTask);
        var correlationContext = Substitute.For<ICorrelationContext>();
        var logger = Substitute.For<ILogger<CorrelationIdLoggingEnricher>>();

        var act = async () => await enricher.InvokeAsync(null!, correlationContext, logger);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task LoggingEnricher_InvokeAsync_WithNullCorrelationContext_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdLoggingEnricher.InvokeAsync line 34: null correlationContext guard.
        var enricher = new CorrelationIdLoggingEnricher(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        var logger = Substitute.For<ILogger<CorrelationIdLoggingEnricher>>();

        var act = async () => await enricher.InvokeAsync(httpContext, null!, logger);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("correlationContext");
    }

    [Fact]
    public async Task LoggingEnricher_InvokeAsync_WithNullLogger_ThrowsArgumentNullException()
    {
        // Exercises CorrelationIdLoggingEnricher.InvokeAsync line 36: null logger guard.
        var enricher = new CorrelationIdLoggingEnricher(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        var correlationContext = Substitute.For<ICorrelationContext>();

        var act = async () => await enricher.InvokeAsync(httpContext, correlationContext, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task LoggingEnricher_InvokeAsync_PushesCorrelationIdToLoggerScope()
    {
        // Exercises CorrelationIdLoggingEnricher.InvokeAsync lines 38-44: logger scope and next() call.
        var nextCalled = false;
        var enricher = new CorrelationIdLoggingEnricher(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        var correlationContext = Substitute.For<ICorrelationContext>();
        correlationContext.CorrelationId.Returns("enricher-corr-id");
        var logger = Substitute.For<ILogger<CorrelationIdLoggingEnricher>>();
        logger.BeginScope(Arg.Any<Dictionary<string, object>>()).Returns(Substitute.For<IDisposable>());

        await enricher.InvokeAsync(httpContext, correlationContext, logger);

        nextCalled.Should().BeTrue();
    }
}
