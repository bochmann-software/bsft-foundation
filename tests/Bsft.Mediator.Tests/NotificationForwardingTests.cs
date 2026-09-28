using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// Covers the mediator half of notification forwarding: when the forwarder is invoked, in which
/// order relative to the local handlers, and how its failures compose.
/// </summary>
public class NotificationForwardingTests
{
    private static ServiceProvider BuildProvider(
        bool forward,
        INotificationForwarder? forwarder,
        params INotificationHandler<ForwardedNotification>[] handlers)
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(mediator =>
        {
            if (forward)
                mediator.ForwardNotifications();
        });

        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        if (forwarder is not null)
            services.AddSingleton(forwarder);

        return services.BuildServiceProvider();
    }

    private static async Task PublishAsync(ServiceProvider provider, ForwardedNotification notification)
    {
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.PublishAsync(notification);
    }

    [Fact]
    public async Task Forwarder_receives_the_notification_when_forwarding_is_configured()
    {
        var forwarder = new RecordingForwarder();
        using var provider = BuildProvider(forward: true, forwarder);

        await PublishAsync(provider, new ForwardedNotification("n1"));

        forwarder.Forwarded.Should().ContainSingle().Which.Should().Be(new ForwardedNotification("n1"));
    }

    [Fact]
    public async Task Forwarder_forwards_under_the_static_notification_type()
    {
        var forwarder = new RecordingForwarder();
        using var provider = BuildProvider(forward: true, forwarder);

        await PublishAsync(provider, new ForwardedNotification("n1"));

        forwarder.ForwardedTypes.Should().ContainSingle()
            .Which.Should().Be(typeof(ForwardedNotification));
    }

    [Fact]
    public async Task Forwarder_is_not_invoked_when_forwarding_is_not_configured()
    {
        var forwarder = new RecordingForwarder();
        using var provider = BuildProvider(forward: false, forwarder);

        await PublishAsync(provider, new ForwardedNotification("n1"));

        forwarder.Forwarded.Should().BeEmpty();
    }

    [Fact]
    public async Task Local_handlers_run_before_the_forwarder()
    {
        var order = new List<string>();
        var forwarder = new RecordingForwarder(order);
        using var provider = BuildProvider(forward: true, forwarder, new RecordingHandler(order));

        await PublishAsync(provider, new ForwardedNotification("n1"));

        order.Should().Equal("handler", "forwarder");
    }

    [Fact]
    public async Task Missing_forwarder_is_reported_instead_of_dropping_the_notification()
    {
        using var provider = BuildProvider(forward: true, forwarder: null);

        var act = () => PublishAsync(provider, new ForwardedNotification("n1"));

        (await act.Should().ThrowAsync<AggregateException>())
            .Which.InnerExceptions.Should().ContainSingle()
            .Which.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("INotificationForwarder");
    }

    [Fact]
    public async Task Unactivatable_forwarder_is_reported_through_the_aggregate_exception()
    {
        // The documented misconfiguration: the bridge is registered but the bus it forwards to is
        // not, so the forwarder fails on activation rather than inside ForwardAsync. That failure
        // must compose like any other forwarding failure instead of escaping PublishAsync raw.
        var services = new ServiceCollection();
        services.AddBsftMediator(mediator => mediator.ForwardNotifications());
        services.AddSingleton<INotificationForwarder, DependentForwarder>();

        using var provider = services.BuildServiceProvider();

        var act = () => PublishAsync(provider, new ForwardedNotification("n1"));

        (await act.Should().ThrowAsync<AggregateException>())
            .Which.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Contain(nameof(DependentForwarder));
    }

    [Fact]
    public async Task Forwarding_failure_composes_with_the_handler_failures()
    {
        var forwarder = new ThrowingForwarder();
        using var provider = BuildProvider(forward: true, forwarder, new ThrowingHandler());

        var act = () => PublishAsync(provider, new ForwardedNotification("n1"));

        var aggregate = (await act.Should().ThrowAsync<AggregateException>()).Which;
        aggregate.InnerExceptions.Should().HaveCount(2);
        aggregate.InnerExceptions.Select(ex => ex.Message).Should()
            .ContainInOrder("the handler is broken", "the forwarder is broken");
    }

    [Fact]
    public async Task Local_handlers_still_run_when_the_forwarder_fails()
    {
        var order = new List<string>();
        using var provider = BuildProvider(forward: true, new ThrowingForwarder(), new RecordingHandler(order));

        var act = () => PublishAsync(provider, new ForwardedNotification("n1"));

        await act.Should().ThrowAsync<AggregateException>();
        order.Should().Equal("handler");
    }
}
