using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public sealed class FirstFailingHandler : INotificationHandler<DoubleFailureNotification>
{
    public Task HandleAsync(DoubleFailureNotification notification, CancellationToken ct = default) =>
        throw new InvalidOperationException("first handler failed");
}
