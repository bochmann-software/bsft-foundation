using Bsft.Messaging.Consuming;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Bsft.Messaging.Tests.Consuming;

public class IntegrationEventHandlerInvokerTests
{
    private readonly IntegrationEventHandlerInvoker _invoker = new();

    private static ServiceProvider BuildProvider(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Every_registered_handler_is_invoked_and_counted()
    {
        var first = new RecordingOrderPlacedHandler();
        var second = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(first);
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(second);
        });

        var count = await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent());

        count.Should().Be(2);
        first.Handled.Should().ContainSingle();
        second.Handled.Should().ContainSingle();
    }

    [Fact]
    public async Task Handlers_run_one_at_a_time_in_registration_order()
    {
        // The delivery hands every handler one service scope, and with the EF Core inbox that scope's
        // DbContext is what they are documented to write through. A DbContext tolerates exactly one
        // operation at a time, so overlapping handlers would fail every attempt of a delivery that has
        // two of them — the trace is what proves they do not overlap.
        var trace = new ConcurrentQueue<string>();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new TracingOrderPlacedHandler(trace, "first"));
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new TracingOrderPlacedHandler(trace, "second"));
        });

        await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent());

        trace.Should().Equal("enter first", "exit first", "enter second", "exit second");
    }

    [Fact]
    public async Task A_delivery_withdrawn_mid_run_does_not_start_the_handlers_after_it()
    {
        // The question sequential invocation newly makes askable. The delivery is going to be
        // abandoned and redelivered, so a handler started now would have its work thrown away and be
        // run a second time on the redelivery for nothing.
        using var cts = new CancellationTokenSource();
        var later = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new WithdrawingOrderPlacedHandler(cts));
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(later);
        });

        var act = async () => await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        later.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task A_withdrawal_supersedes_a_failure_already_recorded()
    {
        // The precedence, pinned rather than left to whichever check happens to come first. The
        // consumer spends a delivery attempt on a failure and none on a shutdown, so reporting the
        // earlier failure here would push the message towards parking every time a restart coincided
        // with a broken handler — and the redelivery meets that handler again anyway.
        using var cts = new CancellationTokenSource();
        var later = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new ThrowingOrderPlacedHandler());
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new WithdrawingOrderPlacedHandler(cts));
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(later);
        });

        var act = async () => await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        later.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_nobody_handles_is_not_an_error()
    {
        using var provider = BuildProvider(_ => { });

        var count = await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent());

        count.Should().Be(0);
    }

    [Fact]
    public async Task Every_handler_runs_even_when_an_earlier_one_throws()
    {
        var surviving = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new ThrowingOrderPlacedHandler());
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(surviving);
        });

        var act = async () => await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent());

        await act.Should().ThrowAsync<MessagePublishException>();
        surviving.Handled.Should().ContainSingle();
    }

    [Fact]
    public async Task A_handler_that_faults_after_an_await_is_reported_too()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new FaultingOrderPlacedHandler()));

        var act = async () => await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent());

        await act.Should().ThrowAsync<MessagePublishException>();
    }

    [Fact]
    public async Task A_handler_cancelled_by_a_token_of_its_own_is_a_failure_and_not_a_withdrawal()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new TimingOutOrderPlacedHandler()));

        var act = async () => await _invoker.InvokeAllAsync(provider, new OrderPlacedEvent());

        await act.Should().ThrowAsync<MessagePublishException>();
    }

    [Fact]
    public async Task A_handler_cancelled_by_the_caller_s_token_surfaces_as_cancellation()
    {
        using var provider = BuildProvider(s =>
            s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new BlockingOrderPlacedHandler()));
        using var cts = new CancellationTokenSource();

        var invoking = _invoker.InvokeAllAsync(provider, new OrderPlacedEvent(), cts.Token);
        await cts.CancelAsync();

        var act = async () => await invoking;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handlers_are_resolved_by_the_event_s_runtime_type()
    {
        var placed = new RecordingOrderPlacedHandler();
        using var provider = BuildProvider(s => s.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(placed));

        // A different event type resolves a different, empty set rather than the one registered above.
        var count = await _invoker.InvokeAllAsync(provider, new OrderShippedEvent());

        count.Should().Be(0);
        placed.Handled.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        using var provider = BuildProvider(_ => { });

        var withoutServices = async () => await _invoker.InvokeAllAsync(null!, new OrderPlacedEvent());
        var withoutEvent = async () => await _invoker.InvokeAllAsync(provider, null!);

        await withoutServices.Should().ThrowAsync<ArgumentNullException>().WithParameterName("handlerServices");
        await withoutEvent.Should().ThrowAsync<ArgumentNullException>().WithParameterName("integrationEvent");
    }
}
