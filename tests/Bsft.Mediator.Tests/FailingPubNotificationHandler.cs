using Bsft.Instrumentation;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public sealed class FailingPubNotificationHandler : INotificationHandler<FailingPubNotification>
{
    public Task HandleAsync(FailingPubNotification notification, CancellationToken ct = default) =>
        throw new InvalidOperationException("handler boom");
}
