using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class IsolationThrowingHandler : INotificationHandler<IsolationNotification>
{
    public Task HandleAsync(IsolationNotification notification, CancellationToken ct = default)
        => throw new InvalidOperationException("notification handler failed");
}
