using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>Records every notification handed to it, and the static type it arrived under.</summary>
public sealed class RecordingForwarder : INotificationForwarder
{
    private readonly List<string>? _order;

    public RecordingForwarder(List<string>? order = null)
    {
        _order = order;
    }

    public List<INotification> Forwarded { get; } = [];

    public List<Type> ForwardedTypes { get; } = [];

    public Task ForwardAsync<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification
    {
        Forwarded.Add(notification);
        ForwardedTypes.Add(typeof(TNotification));
        _order?.Add("forwarder");
        return Task.CompletedTask;
    }
}
