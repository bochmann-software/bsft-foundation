using System.Net;
using Bsft.Idempotency;
using Bsft.Resilience.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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

    [Fact]
    public void IdempotencyKeyHeaderName_MatchesTheNameBsftIdempotencyReads()
        => ResilienceDefaults.IdempotencyKeyHeaderName.Should().Be(IdempotencyDefaults.HeaderName);

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task UnsafeMethod_WithoutIdempotencyKey_IsNotRetried(string method)
    {
        var handler = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var provider = BuildProvider(handler, ZeroDelay);

        var response = await CreateClient(provider).SendAsync(Request(method));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task UnsafeMethod_WithIdempotencyKey_IsRetried(string method)
    {
        var handler = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var provider = BuildProvider(handler, ZeroDelay);
        var request = Request(method);
        request.Headers.Add(ResilienceDefaults.IdempotencyKeyHeaderName, "key-1");

        var response = await CreateClient(provider).SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task SafeOrIdempotentMethod_IsRetried(string method)
    {
        var handler = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var provider = BuildProvider(handler, ZeroDelay);

        var response = await CreateClient(provider).SendAsync(Request(method));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ConsumerOverride_OfShouldHandle_StillWins()
    {
        var handler = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var services = new ServiceCollection();
        services
            .AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddBsftResilience(ZeroDelay);
        services.Configure<HttpStandardResilienceOptions>(
            "test-standard",
            o => o.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable));
        using var provider = services.BuildServiceProvider();

        var response = await CreateClient(provider).SendAsync(Request("POST"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(2);
    }

    private static void ZeroDelay(BsftResilienceOptions o)
    {
        o.MaxRetryAttempts = 3;
        o.BaseDelay = TimeSpan.Zero;
    }

    private static HttpRequestMessage Request(string method)
        => new(new HttpMethod(method), new Uri("https://example.test/data"));

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
