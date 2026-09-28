using Bsft.Correlation.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Correlation.Tests;

/// <summary>
/// What the middleware is allowed to believe from a client. The header is echoed into the response and
/// into the logging scope, so anything it accepts is something an outsider writes into both.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task AWellFormedHeader_IsTakenAsTheRequestsCorrelationId()
    {
        var stamped = await RunAsync("a-well-formed.Id_1");

        stamped.Should().Be("a-well-formed.Id_1");
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("has\r\nboth")]
    [InlineData("trailing-newline\n")]
    [InlineData("trailing-return\r")]
    public async Task AHeaderCarryingAnythingElse_IsReplacedRatherThanEchoed(string header)
    {
        // The trailing newline is the reason the pattern ends in \z: $ also matches just before a
        // single closing newline, so a value ending in one would otherwise reach the response header
        // and the log scope and forge a line in both. The carriage return guards the neighbouring
        // character, which $ already refused.
        var stamped = await RunAsync(header);

        stamped.Should().NotBe(header);
        Guid.TryParse(stamped, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ARequestWithNoHeaderAtAll_IsGivenOne()
    {
        var stamped = await RunAsync(header: null);

        Guid.TryParse(stamped, out _).Should().BeTrue();
    }

    /// <summary>Runs one request through the shipped pipeline.</summary>
    /// <param name="header">The correlation header the client sent, or null for none.</param>
    /// <returns>The id the response carries.</returns>
    private static async Task<string> RunAsync(string? header)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftCorrelation();

        using var provider = services.BuildServiceProvider();
        var pipeline = new ApplicationBuilder(provider).UseBsftCorrelation().Build();

        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        if (header is not null)
            context.Request.Headers["X-Correlation-Id"] = header;

        await pipeline(context);

        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().CorrelationId
            .Should().Be(context.Response.Headers["X-Correlation-Id"].ToString());

        return context.Response.Headers["X-Correlation-Id"].ToString();
    }
}
