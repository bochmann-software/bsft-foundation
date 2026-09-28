using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A forwarder whose target bus fails.</summary>
public sealed class ThrowingForwarder : INotificationForwarder
{
    public Task ForwardAsync<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification =>
        throw new InvalidOperationException("the forwarder is broken");
}
