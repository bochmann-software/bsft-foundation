using Bsft.Correlation;
using Bsft.Correlation.DependencyInjection;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.Correlation.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Xunit;

namespace Bsft.EventAggregator.Saga.Correlation.Tests;

/// <summary>
/// Pins the wiring a host has to get right for a saga step to run under the correlation id of the
/// request that triggered it: <c>AddBsftSagaCorrelation()</c> alone is not enough, the request pipeline
/// needs <c>UseBsftSagaCorrelation()</c> behind <c>UseBsftCorrelation()</c> as well. Omitting the
/// middleware is silent — the step scope simply mints an id of its own — so only a test through a
/// real pipeline tells the two configurations apart.
/// </summary>
public class RequestPipelineCorrelationTests
{
    private const string CORRELATION_HEADER = "X-Correlation-Id";
    private const string REQUEST_ID = "request-from-the-caller";

    public record OrderPlaced(string OrderId);

    public class OrderState;

    /// <summary>Collects the correlation id the step scope reported, which is what is under test.</summary>
    public sealed class CorrelationRecorder
    {
        private readonly ConcurrentQueue<string> _seen = new();

        public void Record(string correlationId) => _seen.Enqueue(correlationId);

        public string? Last => _seen.TryPeek(out var id) ? id : null;
    }

    public class OrderFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((ctx, _) =>
                {
                    ctx.Services.GetRequiredService<CorrelationRecorder>().Record(
                        ctx.Services.GetRequiredService<ICorrelationContext>().CorrelationId);

                    return Task.CompletedTask;
                })
                .Complete();
        }
    }

    [Fact]
    public async Task WithTheMiddleware_AStepTriggeredByARequestRunsUnderTheRequestsId()
    {
        using var host = await StartHostAsync(isMiddlewareRegistered: true);

        await PlaceOrderAsync(host, "order-1");

        host.Services.GetRequiredService<CorrelationRecorder>().Last.Should().Be(REQUEST_ID);
    }

    [Fact]
    public async Task WithoutTheMiddleware_TheStepFallsBackToAnIdOfItsOwn()
    {
        using var host = await StartHostAsync(isMiddlewareRegistered: false);

        await PlaceOrderAsync(host, "order-2");

        var recorded = host.Services.GetRequiredService<CorrelationRecorder>().Last;
        recorded.Should().NotBe(REQUEST_ID);
        Guid.TryParse(recorded, out _).Should().BeTrue();
    }

    private static async Task PlaceOrderAsync(IHost host, string orderId)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, REQUEST_ID);

        var response = await client.GetAsync(new Uri($"/orders/{orderId}", UriKind.Relative));

        response.EnsureSuccessStatusCode();
    }

    private static async Task<IHost> StartHostAsync(bool isMiddlewareRegistered)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddBsftCorrelation();
                    services.AddSingleton<CorrelationRecorder>();
                    services.AddBsftEventAggregator();
                    services.AddBsftSaga();
                    services.AddBsftSagaCorrelation();
                    services.AddBsftSagaFlow<OrderFlow>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseBsftCorrelation();

                    if (isMiddlewareRegistered)
                        app.UseBsftSagaCorrelation();

                    app.UseEndpoints(endpoints => endpoints.MapGet(
                        "/orders/{orderId}",
                        async (string orderId, IEventAggregator events) =>
                        {
                            await events.PublishAsync(new OrderPlaced(orderId));
                            return Results.Ok();
                        }));
                });
            })
            .StartAsync();

        host.Services.UseBsftSagaFlows();

        return host;
    }
}
