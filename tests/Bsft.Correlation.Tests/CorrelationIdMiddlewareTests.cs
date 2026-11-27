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
    [InlineData("")]
    [InlineData("\u00e9")]
    [InlineData("\u0661")]
    [InlineData("has\0nul")]
    [InlineData("has spaces")]
    [InlineData("has\r\nboth")]
    [InlineData("trailing-newline\n")]
    [InlineData("trailing-return\r")]
    public async Task AHeaderCarryingAnythingElse_IsReplacedRatherThanEchoed(string header)
    {
        var stamped = await RunAsync(header);

        stamped.Should().NotBe(header);
        Guid.TryParse(stamped, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task HeaderLength_IsBounded(int length, bool accepted)
    {
        var header = new string('a', length);
        var stamped = await RunAsync(header);
        (stamped == header).Should().Be(accepted);
        if (!accepted)
            Guid.TryParse(stamped, out _).Should().BeTrue();
    }

    [Fact]
    public async Task EveryAsciiCharacter_IsCheckedAgainstTheExactAlphabet()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-";
        for (var value = 0; value < 128; value++)
        {
            var character = (char)value;
            var stamped = await RunAsync("a" + character);
            (stamped == "a" + character).Should().Be(alphabet.Contains(character), "ASCII value {0}", value);
        }
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
