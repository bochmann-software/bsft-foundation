using Bsft.EventAggregator;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.Mediator.EventAggregator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.EventAggregator.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftMediatorEventAggregator_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftMediatorEventAggregator();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftMediatorEventAggregator_RegistersTheForwarder()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();

        services.AddBsftMediatorEventAggregator();

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<INotificationForwarder>()
            .Should().BeOfType<EventAggregatorNotificationForwarder>();
    }

    [Fact]
    public void AddBsftMediatorEventAggregator_KeepsAnExistingForwarderRegistration()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();
        services.AddSingleton<INotificationForwarder, CustomForwarder>();

        services.AddBsftMediatorEventAggregator();

        using var sp = services.BuildServiceProvider();
        sp.GetServices<INotificationForwarder>().Should().ContainSingle()
            .Which.Should().BeOfType<CustomForwarder>();
    }

    private sealed class CustomForwarder : INotificationForwarder
    {
        public Task ForwardAsync<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
