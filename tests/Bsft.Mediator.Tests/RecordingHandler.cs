using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A local handler that records that it ran.</summary>
public sealed class RecordingHandler : INotificationHandler<ForwardedNotification>
{
    private readonly List<string> _order;

    public RecordingHandler(List<string> order)
    {
        _order = order ?? throw new ArgumentNullException(nameof(order));
    }

    public Task HandleAsync(ForwardedNotification notification, CancellationToken ct = default)
    {
        _order.Add("handler");
        return Task.CompletedTask;
    }
}
