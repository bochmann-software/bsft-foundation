using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A local handler that fails, so its failure can be composed with a forwarding failure.</summary>
public sealed class ThrowingHandler : INotificationHandler<ForwardedNotification>
{
    public Task HandleAsync(ForwardedNotification notification, CancellationToken ct = default) =>
        throw new InvalidOperationException("the handler is broken");
}
