using Bsft.Correlation;
using Bsft.Resilience.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using Xunit;

namespace Bsft.Resilience.Tests;

public class ResilientHttpClientRegistrationTests
{
    [Fact]
    public void AddBsftResilientHttpClient_NullServices_Throws()
    {
        var act = () => Bsft.Resilience.DependencyInjection.ResilienceServiceCollectionExtensions.AddBsftResilientHttpClient(null!, "client");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddBsftResilientHttpClient_BlankName_Throws(string? name)
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftResilientHttpClient(name!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ResilientClient_PropagatesCorrelationIdToEveryAttempt()
    {
        var handler = new SequencedHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        var correlationContext = Substitute.For<ICorrelationContext>();
        correlationContext.CorrelationId.Returns("cid-123");

        var services = new ServiceCollection();
        services.AddSingleton(correlationContext);
        services
            .AddBsftResilientHttpClient("downstream", o =>
            {
                o.MaxRetryAttempts = 3;
                o.BaseDelay = TimeSpan.Zero;
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("downstream");

        var response = await client.GetAsync(new Uri("https://example.test/data"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(3);
        // The header is stamped once and carried on the retry clones, so all three attempts
        // (initial + 2 retries) reach the downstream tagged with the correlation ID.
        handler.Requests
            .Select(r => r.Headers.GetValues("X-Correlation-Id").Single())
            .Should().Equal("cid-123", "cid-123", "cid-123");
    }
}
