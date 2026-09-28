using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class TestNotificationHandler1 : INotificationHandler<TestNotification>
{
    public static List<string> Messages { get; } = [];

    public Task HandleAsync(TestNotification notification, CancellationToken ct = default)
    {
        Messages.Add($"Handler1: {notification.Message}");
        return Task.CompletedTask;
    }

    public static void Reset() => Messages.Clear();
}
