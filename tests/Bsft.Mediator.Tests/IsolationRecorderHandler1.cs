using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class IsolationRecorderHandler1 : INotificationHandler<IsolationNotification>
{
    public static List<string> Messages { get; } = [];

    public Task HandleAsync(IsolationNotification notification, CancellationToken ct = default)
    {
        Messages.Add($"Recorder1: {notification.Message}");
        return Task.CompletedTask;
    }

    public static void Reset() => Messages.Clear();
}
