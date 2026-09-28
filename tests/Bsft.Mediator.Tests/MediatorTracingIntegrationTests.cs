using Bsft.Instrumentation;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public sealed class MediatorTracingIntegrationTests
{
    [Fact]
    public void AddBsftMediator_RegistersTelemetryAndAdvertisesSource()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(c => c.EnableTracing());

        using var sp = services.BuildServiceProvider();

        sp.GetService<IMediatorTelemetry>().Should().NotBeNull();
        sp.GetServices<ObservabilitySourceRegistration>().Should().Contain(r => r.Name == "Bsft.Mediator");
    }

    [Fact]
    public void EnableTracing_RegistersTracingBehaviorAsOutermost()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(c => c.EnableTracing());
        // A user-registered behavior added afterwards must still be wrapped by the tracing span.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(NoopBehavior<,>));

        using var sp = services.BuildServiceProvider();
        var behaviors = sp.GetServices<IPipelineBehavior<TestCommand, string>>().ToList();

        behaviors.Should().HaveCount(2);
        behaviors.First().Should().BeOfType<TracingBehavior<TestCommand, string>>();
    }

    [Fact]
    public void WithoutEnableTracing_NoTracingBehaviorRegistered()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(NoopBehavior<,>));

        using var sp = services.BuildServiceProvider();
        var behaviors = sp.GetServices<IPipelineBehavior<TestCommand, string>>().ToList();

        behaviors.Should().NotContain(b => b is TracingBehavior<TestCommand, string>);
    }

    [Fact]
    public async Task PublishAsync_WithTracingEnabled_EmitsNotificationSpan()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(c =>
        {
            c.RegisterServicesFromAssembly(typeof(TracingPubNotification).Assembly);
            c.EnableTracing();
        });
        using var sp = services.BuildServiceProvider();
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        await sp.GetRequiredService<IMediator>().PublishAsync(new TracingPubNotification("hi"));

        capture.Activities.Should().Contain(a => a.DisplayName == "TracingPubNotification");
    }

    [Fact]
    public async Task PublishAsync_WithTracingDisabled_EmitsNoSpan()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(c => c.RegisterServicesFromAssembly(typeof(TracingPubNotification).Assembly));
        using var sp = services.BuildServiceProvider();
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        await sp.GetRequiredService<IMediator>().PublishAsync(new TracingPubNotification("hi"));

        capture.Activities.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WithTracingEnabled_FailingHandler_RecordsErrorSpan()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(c =>
        {
            c.RegisterServicesFromAssembly(typeof(FailingPubNotification).Assembly);
            c.EnableTracing();
        });
        using var sp = services.BuildServiceProvider();
        using var capture = new TelemetryCapture(MediatorTelemetry.SourceName);

        var act = () => sp.GetRequiredService<IMediator>().PublishAsync(new FailingPubNotification("hi"));

        await act.Should().ThrowAsync<AggregateException>();
        capture.Activities.Should().Contain(a =>
            a.DisplayName == "FailingPubNotification" && a.Status == System.Diagnostics.ActivityStatusCode.Error);
    }
}
