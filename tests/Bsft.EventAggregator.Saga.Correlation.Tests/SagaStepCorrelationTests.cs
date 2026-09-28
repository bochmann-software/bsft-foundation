using Bsft.Correlation;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.Correlation.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Bsft.EventAggregator.Saga.Correlation.Tests;

/// <summary>
/// A saga step runs in a service scope the flow host creates from the root provider, so without the
/// bridge it reports a correlation id minted for that scope rather than the id of whatever caused
/// the step. These tests pin the id a step actually observes through <see cref="ICorrelationContext"/>
/// on both paths into a step.
/// </summary>
public class SagaStepCorrelationTests
{
    public record OrderPlaced(string OrderId);

    public record OrderConfirmed(string OrderId);

    public class OrderState;

    /// <summary>Collects the correlation id each step scope reported, which is what is under test.</summary>
    public sealed class CorrelationRecorder
    {
        private readonly ConcurrentDictionary<string, string> _seen = new();

        public void Record(string step, string correlationId) => _seen[step] = correlationId;

        public string? this[string step] => _seen.TryGetValue(step, out var id) ? id : null;
    }

    public class OrderFlow : ISagaFlow
    {
        public void Configure(ISagaBuilderFactory factory)
        {
            factory.WithState<OrderState>()
                .StartWith<OrderPlaced>(e => e.OrderId)
                .Then((ctx, _) => RecordAsync(ctx.Services, "trigger"))
                .WaitFor<OrderConfirmed>(e => e.OrderId)
                .Then((ctx, _) => RecordAsync(ctx.Services, "follow-up"))
                .Complete();
        }

        private static Task RecordAsync(IServiceProvider stepServices, string step)
        {
            var correlationId = stepServices.GetRequiredService<ICorrelationContext>().CorrelationId;
            stepServices.GetRequiredService<CorrelationRecorder>().Record(step, correlationId);

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AStepTriggeredFromARequestScope_ObservesTheRequestsCorrelationId()
    {
        using var provider = BuildProvider();

        // What SagaCorrelationMiddleware does for the duration of a request.
        using (Ambient(provider).Use("request-7"))
        {
            await Events(provider).PublishAsync(new OrderPlaced("order-1"));
        }

        Recorder(provider)["trigger"].Should().Be("request-7");
    }

    [Fact]
    public async Task ASubsequentStepTriggeredFromAnotherRequest_ObservesThatRequestsCorrelationId()
    {
        using var provider = BuildProvider();

        using (Ambient(provider).Use("request-7"))
        {
            await Events(provider).PublishAsync(new OrderPlaced("order-2"));
        }

        // A later request confirming the order is its own correlation, and the step it wakes belongs
        // to that request rather than to the one that started the saga.
        using (Ambient(provider).Use("request-8"))
        {
            await Events(provider).PublishAsync(new OrderConfirmed("order-2"));
        }

        Recorder(provider)["trigger"].Should().Be("request-7");
        Recorder(provider)["follow-up"].Should().Be("request-8");
    }

    [Fact]
    public async Task AStepWokenUnderARestoredTimeoutCorrelation_KeepsThatId()
    {
        using var provider = BuildProvider();

        using (Ambient(provider).Use("request-7"))
        {
            await Events(provider).PublishAsync(new OrderPlaced("order-3"));
        }

        // SagaTimeoutPublisher restores the scheduling request's id around the publish, exactly as
        // this does; the woken step has to report that id and not one minted for its own scope.
        using (Ambient(provider).Use("scheduled-under-9"))
        {
            await Events(provider).PublishAsync(new OrderConfirmed("order-3"));
        }

        Recorder(provider)["follow-up"].Should().Be("scheduled-under-9");
    }

    [Fact]
    public async Task AStepTriggeredWithNoAmbientCorrelation_GetsAFreshId()
    {
        using var provider = BuildProvider();

        await Events(provider).PublishAsync(new OrderPlaced("order-4"));

        // The step scope mints its own id, which still has to be a usable one.
        var recorded = Recorder(provider)["trigger"];
        recorded.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(recorded, out _).Should().BeTrue();
    }

    private static IEventAggregator Events(IServiceProvider provider) =>
        provider.GetRequiredService<IEventAggregator>();

    private static ISagaCorrelationScope Ambient(IServiceProvider provider) =>
        provider.GetRequiredService<ISagaCorrelationScope>();

    private static CorrelationRecorder Recorder(IServiceProvider provider) =>
        provider.GetRequiredService<CorrelationRecorder>();

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddSingleton<CorrelationRecorder>();

        // Mirrors AddBsftCorrelation: one scoped context per scope, minting its own id by default.
        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        services.AddBsftSaga();
        services.AddBsftSagaCorrelation();
        services.AddBsftSagaFlow<OrderFlow>();

        var provider = services.BuildServiceProvider();
        provider.UseBsftSagaFlows();

        return provider;
    }
}
