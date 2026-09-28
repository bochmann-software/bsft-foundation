using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A forwarder that cannot be activated because its dependency is not registered.</summary>
public sealed class DependentForwarder : INotificationForwarder
{
    private readonly IUnregisteredBus _bus;

    public DependentForwarder(IUnregisteredBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public Task ForwardAsync<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification =>
        _bus.SendAsync(notification);
}
