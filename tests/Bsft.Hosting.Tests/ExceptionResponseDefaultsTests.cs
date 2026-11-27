using Bsft.Core;
using Bsft.Hosting.Tests.Fakes;
using Bsft.Hosting.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using Xunit;

namespace Bsft.Hosting.Tests;

/// <summary>
/// Pins the response bodies the middleware writes when nothing is registered for the exception
/// extension points, so the additive extension points cannot change them.
/// </summary>
public class ExceptionResponseDefaultsTests
{
    [Fact]
    public async Task InvokeAsync_UnhandledException_WritesTheFixedBodyWithoutTheExceptionText()
    {
        using var host = await ExceptionHost.StartAsync(false, _ => throw new InvalidOperationException("secret connection string"));

        var response = await host.GetTestClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://httpstatuses.io/500\",\"title\":\"Internal Server Error\",\"status\":500," +
            "\"detail\":\"An unexpected error occurred. Please use the correlation ID for troubleshooting.\"," +
            "\"correlationId\":\"test-correlation-id\"}");
        body.Should().NotContain("secret");
    }

    [Fact]
    public async Task InvokeAsync_UnmappedBsftException_WritesTheFixedBodyWithoutTheExceptionText()
    {
        using var host = await ExceptionHost.StartAsync(false, _ => throw new BsftException("secret internals", "CODE"));

        var body = await (await host.GetTestClient().GetAsync("/")).Content.ReadAsStringAsync();

        body.Should().Be(
            "{\"type\":\"https://httpstatuses.io/500\",\"title\":\"Internal Server Error\",\"status\":500," +
            "\"detail\":\"An unexpected error occurred. Please use the correlation ID for troubleshooting.\"," +
            "\"correlationId\":\"test-correlation-id\"}");
    }

    [Fact]
    public async Task InvokeAsync_ForbiddenException_WritesTheDocumentFieldForField()
    {
        using var host = await ExceptionHost.StartAsync(false, _ => throw new ForbiddenException("no scope"));

        var response = await host.GetTestClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Retry-After").Should().BeFalse();
        body.Should().StartWith("{\"type\":\"https://httpstatuses.io/403\",\"title\":\"Forbidden\",\"status\":403,\"detail\":\"no scope\",\"errorCode\":");
        body.Should().EndWith("\"correlationId\":\"test-correlation-id\"}");
    }
}
