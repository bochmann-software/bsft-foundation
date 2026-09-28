using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public sealed class SecondFailingHandler : INotificationHandler<DoubleFailureNotification>
{
    public Task HandleAsync(DoubleFailureNotification notification, CancellationToken ct = default) =>
        throw new InvalidOperationException("second handler failed");
}
