using Bsft.Messaging.Publishing;
using Bsft.Messaging.RabbitMQ.Publishing;
using Bsft.Messaging.RabbitMQ.Tests.Fakes;
using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client.Exceptions;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests.Publishing;

public class RabbitMQMessageBusTests
{
    private readonly FakeRabbitMQPublishChannelProvider _channelProvider = new();
    private readonly BsftRabbitMQOptions _options = TestOptions.Valid();

    [Fact]
    public void A_null_dependency_is_rejected()
    {
        var settings = new RabbitMQConnectionSettings(_options);
        var topology = new RabbitMQTopology(_options);
        var mapper = new RabbitMQMessageMapper(settings);
        var logger = NullLogger<RabbitMQMessageBus>.Instance;

        var withoutProvider = () => new RabbitMQMessageBus(null!, mapper, topology, settings, logger);
        var withoutMapper = () => new RabbitMQMessageBus(_channelProvider, null!, topology, settings, logger);
        var withoutTopology = () => new RabbitMQMessageBus(_channelProvider, mapper, null!, settings, logger);
        var withoutSettings = () => new RabbitMQMessageBus(_channelProvider, mapper, topology, null!, logger);
        var withoutLogger = () => new RabbitMQMessageBus(_channelProvider, mapper, topology, settings, null!);

        withoutProvider.Should().Throw<ArgumentNullException>();
        withoutMapper.Should().Throw<ArgumentNullException>();
        withoutTopology.Should().Throw<ArgumentNullException>();
        withoutSettings.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_null_message_is_rejected()
    {
        var publish = () => Bus().PublishAsync(null!);

        await publish.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task A_message_without_an_event_name_is_rejected_because_it_has_no_routing_key()
    {
        var publish = () => Bus().PublishAsync(Message() with { EventName = "  " });

        await publish.Should().ThrowAsync<ArgumentException>().WithParameterName("message");
    }

    [Fact]
    public async Task The_message_goes_to_the_applications_exchange_under_the_event_name()
    {
        var message = Message();

        await Bus().PublishAsync(message);

        var published = _channelProvider.Channel.Published.Should().ContainSingle().Subject;
        published.Exchange.Should().Be("orders.events");
        published.RoutingKey.Should().Be("orders.placed.v1");
        published.Properties.MessageId.Should().Be(message.EventId.ToString());
        Encoding.UTF8.GetString(published.Body).Should().Be(message.Payload);
    }

    [Fact]
    public async Task A_publish_returns_only_after_the_broker_has_confirmed_it()
    {
        var confirmed = new TaskCompletionSource();
        _channelProvider.Channel.PublishBehaviour = _ => confirmed.Task;

        var publish = Bus().PublishAsync(Message());

        publish.IsCompleted.Should().BeFalse();
        confirmed.SetResult();
        await publish;
        publish.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task A_broker_that_refuses_the_message_becomes_a_publish_failure_the_outbox_can_retry()
    {
        var rejection = new OperationInterruptedException();
        _channelProvider.Channel.PublishBehaviour = _ => Task.FromException(rejection);

        var publish = () => Bus().PublishAsync(Message());

        var thrown = await publish.Should().ThrowAsync<MessagePublishException>()
            .WithMessage("*the broker rejected it*");

        thrown.And.InnerException.Should().BeSameAs(rejection);
    }

    [Fact]
    public async Task A_confirmation_that_never_arrives_becomes_a_publish_failure()
    {
        _options.PublisherConfirmTimeout = TimeSpan.FromMilliseconds(20);
        _channelProvider.Channel.PublishBehaviour = ct => Task.Delay(Timeout.Infinite, ct);

        var publish = () => Bus().PublishAsync(Message());

        await publish.Should().ThrowAsync<MessagePublishException>()
            .WithMessage("*did not confirm it within*");
    }

    [Fact]
    public async Task A_publish_the_caller_withdrew_stays_a_cancellation_rather_than_becoming_a_failure()
    {
        using var withdrawn = new CancellationTokenSource();
        _channelProvider.Channel.PublishBehaviour = async ct =>
        {
            await withdrawn.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
        };

        var publish = () => Bus().PublishAsync(Message(), withdrawn.Token);

        await publish.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_unreachable_broker_becomes_a_publish_failure_rather_than_a_transport_exception()
    {
        var unreachable = new BrokerUnreachableException(new SocketException(10061));
        _channelProvider.GetChannelFailure = unreachable;

        var publish = () => Bus().PublishAsync(Message());

        var thrown = await publish.Should().ThrowAsync<MessagePublishException>()
            .WithMessage("*could not be reached*");

        thrown.And.InnerException.Should().BeSameAs(unreachable);
    }

    [Fact]
    public async Task A_socket_that_died_under_the_confirmation_becomes_a_publish_failure()
    {
        // The confirmation is outstanding when the socket goes, which the client raises as an
        // IOException rather than as a protocol exception. Untranslated it would reach the outbox as a
        // transport exception rather than the failed attempt it is.
        _channelProvider.Channel.PublishBehaviour = _ => Task.FromException(new IOException("the socket is gone"));

        var publish = () => Bus().PublishAsync(Message());

        await publish.Should().ThrowAsync<MessagePublishException>().WithMessage("*could not be reached*");
    }

    [Fact]
    public async Task A_provider_disposed_under_the_publish_becomes_a_publish_failure()
    {
        // A shutdown reaching the publishing side before the outbox dispatcher disposes the provider
        // while a row is still being published. The row is untouched, so this is the outbox's failure.
        _channelProvider.GetChannelFailure = new ObjectDisposedException("provider");

        var publish = () => Bus().PublishAsync(Message());

        await publish.Should().ThrowAsync<MessagePublishException>().WithMessage("*could not be reached*");
    }

    [Fact]
    public async Task A_broker_that_never_answers_an_rpc_becomes_a_publish_failure_rather_than_a_raw_timeout()
    {
        // The client raises a plain TimeoutException on its continuation timeout, which is neither a
        // RabbitMQClientException nor an IOException; untranslated it would reach the outbox as a
        // transport exception rather than the classified publish failure it is.
        _channelProvider.Channel.PublishBehaviour = _ => Task.FromException(new TimeoutException("no reply"));

        var publish = () => Bus().PublishAsync(Message());

        await publish.Should().ThrowAsync<MessagePublishException>()
            .WithMessage("*did not respond in time*");
    }

    [Fact]
    public async Task A_broker_that_never_answers_while_the_channel_is_opened_becomes_a_publish_failure()
    {
        _channelProvider.GetChannelFailure = new TimeoutException("no reply");

        var publish = () => Bus().PublishAsync(Message());

        await publish.Should().ThrowAsync<MessagePublishException>()
            .WithMessage("*did not respond in time*");
    }

    [Fact]
    public async Task A_protocol_failure_while_opening_the_channel_becomes_a_publish_failure_too()
    {
        _channelProvider.GetChannelFailure = new OperationInterruptedException();

        var publish = () => Bus().PublishAsync(Message());

        await publish.Should().ThrowAsync<MessagePublishException>().WithMessage("*could not be reached*");
    }

    private RabbitMQMessageBus Bus()
    {
        var settings = new RabbitMQConnectionSettings(_options);

        return new RabbitMQMessageBus(
            _channelProvider,
            new RabbitMQMessageMapper(settings),
            new RabbitMQTopology(_options),
            settings,
            NullLogger<RabbitMQMessageBus>.Instance);
    }

    private static IntegrationEventMessage Message() => new()
    {
        EventId = Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = DateTimeOffset.UtcNow,
        CorrelationId = "correlation-1",
        Payload = "{\"orderNumber\":\"A-1\"}",
    };
}
