using System.Net;
using Bsft.Resilience.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Xunit;

namespace Bsft.Resilience.Tests.DependencyInjection;

public class ResilientHttpClientBuilderExtensionsTests
{
    [Fact]
    public void AddBsftResilience_NullBuilder_Throws()
    {
        var act = () => ResilientHttpClientBuilderExtensions.AddBsftResilience(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task TransientResponses_AreRetriedUntilSuccess()
    {
        var handler = new SequencedHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        using var provider = BuildProvider(handler, o =>
        {
            o.MaxRetryAttempts = 3;
            o.BaseDelay = TimeSpan.Zero;
        });
        var client = CreateClient(provider);

        var response = await client.GetAsync(new Uri("https://example.test/data"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task PersistentFailure_ExhaustsRetries_AndReturnsLastResponse()
    {
        var handler = new SequencedHandler(HttpStatusCode.ServiceUnavailable);

        using var provider = BuildProvider(handler, o =>
        {
            o.MaxRetryAttempts = 2;
            o.BaseDelay = TimeSpan.Zero;
        });
        var client = CreateClient(provider);

        var response = await client.GetAsync(new Uri("https://example.test/data"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuitBreaker()
    {
        var handler = new SequencedHandler(HttpStatusCode.ServiceUnavailable);

        using var provider = BuildProvider(handler, o =>
        {
            o.MaxRetryAttempts = 2;
            o.BaseDelay = TimeSpan.Zero;
            o.AttemptTimeout = TimeSpan.FromSeconds(1);
            o.TotalRequestTimeout = TimeSpan.FromSeconds(5);
            // Large sampling window so the breaker trips on failure throughput, not wall-clock timing.
            o.SamplingDuration = TimeSpan.FromSeconds(60);
            o.MinimumThroughput = 2;
            o.FailureRatio = 1.0;
            o.BreakDuration = TimeSpan.FromSeconds(5);
        });
        var client = CreateClient(provider);

        var brokenCircuitObserved = false;

        for (var i = 0; i < 10 && !brokenCircuitObserved; i++)
        {
            try
            {
                await client.GetAsync(new Uri("https://example.test/data"));
            }
            catch (BrokenCircuitException)
            {
                brokenCircuitObserved = true;
            }
        }

        brokenCircuitObserved.Should().BeTrue();
    }

    private static ServiceProvider BuildProvider(SequencedHandler handler, Action<BsftResilienceOptions> configure)
    {
        var services = new ServiceCollection();

        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddBsftResilience(configure);

        return services.BuildServiceProvider();
    }

    private static HttpClient CreateClient(ServiceProvider provider) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
}
