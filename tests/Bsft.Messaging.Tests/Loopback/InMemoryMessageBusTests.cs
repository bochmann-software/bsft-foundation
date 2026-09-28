using Bsft.Messaging.Consuming;
using Bsft.Messaging.Loopback;
using Bsft.Messaging.Publishing;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Bsft.Messaging.Tests.Loopback;

public class InMemoryMessageBusTests
{
    private readonly MessageCorrelationScope _correlationScope = new();
    private readonly IIntegrationEventSerializer _serializer;

    public InMemoryMessageBusTests()
    {
        var options = new BsftMessagingOptions();
        options.AddEvent<OrderPlacedEvent>("orders.placed.v1");
        _serializer = new JsonIntegrationEventSerializer(new IntegrationEventTypeRegistry(TestMessagingOptions.For(options)));
    }

    private readonly IIntegrationEventHandlerInvoker _invoker = new IntegrationEventHandlerInvoker();

    private InMemoryMessageBus CreateBus(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            _serializer,
            _correlationScope,
            _invoker,
            NullLogger<InMemoryMessageBus>.Instance);

    private static IntegrationEventMessage WireMessage(OrderPlacedEvent? integrationEvent = null, string? correlationId = null)
    {
        var payload = integrationEvent ?? new OrderPlacedEvent();

        return new IntegrationEventMessage
        {
            EventId = payload.EventId,
            EventName = "orders.placed.v1",
            OccurredAt = payload.OccurredAt,
            CorrelationId = correlationId,
            Payload = JsonSerializer.Serialize(payload),
        };
    }

    [Fact]
    public async Task An_event_reaches_its_handler()
    {
        var handler = new RecordingOrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler);
        using var provider = services.BuildServiceProvider();
        var integrationEvent = new OrderPlacedEvent { OrderNumber = "A-1" };

        await CreateBus(provider).PublishAsync(WireMessage(integrationEvent));

        handler.Handled.Should().ContainSingle().Which.Should().Be(integrationEvent);
    }

    [Fact]
    public async Task Every_handler_of_the_event_is_invoked()
    {
        var first = new RecordingOrderPlacedHandler();
        var second = new RecordingOrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(first);
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(second);
        using var provider = services.BuildServiceProvider();

        await CreateBus(provider).PublishAsync(WireMessage());

        first.Handled.Should().HaveCount(1);
        second.Handled.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_event_without_a_handler_in_this_process_is_not_an_error()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_correlation_id_of_the_message_is_restored_while_the_handler_runs()
    {
        var handler = new CorrelationRecordingOrderPlacedHandler(_correlationScope);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler);
        using var provider = services.BuildServiceProvider();

        await CreateBus(provider).PublishAsync(WireMessage(correlationId: "request-1"));

        // This is the seam a consumer host uses to put the producing request's id back into the
        // handling scope's logging and audit trail.
        handler.ObservedCorrelationId.Should().Be("request-1");
    }

    [Fact]
    public async Task The_ambient_correlation_id_is_restored_again_afterwards()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        using (_correlationScope.Use("outer"))
        {
            await CreateBus(provider).PublishAsync(WireMessage(correlationId: "request-1"));

            _correlationScope.Current.Should().Be("outer");
        }

        _correlationScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task A_message_without_a_correlation_id_leaves_the_handler_without_one()
    {
        var handler = new CorrelationRecordingOrderPlacedHandler(_correlationScope);
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(handler);
        using var provider = services.BuildServiceProvider();

        // A row written before the outbox recorded correlation ids: it must not inherit whatever id
        // happens to be ambient in the dispatcher.
        using (_correlationScope.Use("some-other-message"))
        {
            await CreateBus(provider).PublishAsync(WireMessage(correlationId: null));
        }

        handler.ObservedCorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task An_event_name_this_process_does_not_know_is_refused()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage() with { EventName = "orders.retired.v1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not registered*");
    }

    [Fact]
    public async Task A_payload_that_decodes_to_nothing_is_refused()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage() with { Payload = "null" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*did not decode*");
    }

    [Fact]
    public async Task A_failing_handler_surfaces_as_a_publish_failure_that_keeps_the_cause()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new FaultingOrderPlacedHandler());
        using var provider = services.BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage());

        var thrown = await act.Should().ThrowAsync<MessagePublishException>();
        thrown.Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Be("handler faulted after an await");
    }

    [Fact]
    public async Task A_handler_that_throws_before_its_first_await_fails_the_same_way()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new ThrowingOrderPlacedHandler());
        using var provider = services.BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage());

        await act.Should().ThrowAsync<MessagePublishException>();
    }

    [Fact]
    public async Task One_broken_handler_does_not_stop_the_others_from_seeing_the_event()
    {
        var healthy = new RecordingOrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new FaultingOrderPlacedHandler());
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(healthy);
        using var provider = services.BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage());

        await act.Should().ThrowAsync<MessagePublishException>();
        healthy.Handled.Should().HaveCount(1);
    }

    [Fact]
    public async Task Cancellation_surfaces_as_cancellation_and_not_as_a_handler_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new BlockingOrderPlacedHandler());
        using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource();

        var publish = CreateBus(provider).PublishAsync(WireMessage(), cts.Token);
        await cts.CancelAsync();

        var act = async () => await publish;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_handler_cancelled_by_a_token_of_its_own_is_a_publish_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new TimingOutOrderPlacedHandler());
        using var provider = services.BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage(), CancellationToken.None);

        // Nobody asked this publish to stop, so the handler's timeout is a failed delivery and has to
        // reach the outbox as one instead of escaping as raw cancellation.
        var thrown = await act.Should().ThrowAsync<MessagePublishException>();
        thrown.Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().ContainSingle()
            .Which.Should().BeOfType<MessagePublishException>()
            .Which.InnerException.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task A_handler_that_times_out_does_not_stop_the_others_from_seeing_the_event()
    {
        var healthy = new RecordingOrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(new TimingOutOrderPlacedHandler());
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(healthy);
        using var provider = services.BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(WireMessage());

        await act.Should().ThrowAsync<MessagePublishException>();
        healthy.Handled.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_handler_that_observes_the_callers_token_still_surfaces_as_cancellation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedEvent>>(
            new CancelingOnShutdownOrderPlacedHandler());
        using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource();

        var publish = CreateBus(provider).PublishAsync(WireMessage(), cts.Token);
        await cts.CancelAsync();

        var act = async () => await publish;

        // The caller withdrew the publish, so even a handler that ends on its own token is not
        // reported as a failure: there is nothing left to retry.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Publishing_null_is_rejected()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = async () => await CreateBus(provider).PublishAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = NullLogger<InMemoryMessageBus>.Instance;

        var withoutScopeFactory = () => new InMemoryMessageBus(null!, _serializer, _correlationScope, _invoker, logger);
        var withoutSerializer = () => new InMemoryMessageBus(scopeFactory, null!, _correlationScope, _invoker, logger);
        var withoutCorrelationScope = () => new InMemoryMessageBus(scopeFactory, _serializer, null!, _invoker, logger);
        var withoutInvoker = () => new InMemoryMessageBus(scopeFactory, _serializer, _correlationScope, null!, logger);
        var withoutLogger = () => new InMemoryMessageBus(scopeFactory, _serializer, _correlationScope, _invoker, null!);

        withoutScopeFactory.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        withoutSerializer.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
        withoutCorrelationScope.Should().Throw<ArgumentNullException>().WithParameterName("correlationScope");
        withoutInvoker.Should().Throw<ArgumentNullException>().WithParameterName("invoker");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
