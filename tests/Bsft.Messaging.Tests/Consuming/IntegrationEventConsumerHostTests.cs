using Bsft.Messaging.Consuming;
using Bsft.Messaging.Tests.Fakes;
using Bsft.Messaging.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Messaging.Tests.Consuming;

public class IntegrationEventConsumerHostTests
{
    private readonly FakeMessageSource _source = new();
    private readonly FakeIntegrationEventConsumer _consumer = new();

    private IntegrationEventConsumerHost CreateHost(ConsumerOptions? options = null) =>
        new(_source,
            _consumer,
            TestMessagingOptions.For(options ?? new ConsumerOptions
            {
                ConsumerName = "orders",
                ReceiveRestartDelay = TimeSpan.FromMilliseconds(1),
            }),
            NullLogger<IntegrationEventConsumerHost>.Instance);

    private static FakeMessageDelivery Delivery() => new(new IntegrationEventMessage
    {
        EventId = Guid.NewGuid(),
        EventName = "orders.placed.v1",
        OccurredAt = DateTimeOffset.UnixEpoch,
        Payload = "{}",
    });

    [Fact]
    public async Task Every_delivery_of_the_stream_reaches_the_consumer()
    {
        _source.Enqueue(Delivery(), Delivery());

        await CreateHost().ReceiveAllAsync();

        _consumer.Consumed.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_loop_re_subscribes_after_the_stream_ends_on_its_own()
    {
        using var cts = new CancellationTokenSource();
        _source.OnSubscribe = subscriptions =>
        {
            if (subscriptions >= 3)
                cts.Cancel();
        };

        await CreateHost().RunLoopAsync(cts.Token);

        _source.Subscriptions.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task The_loop_keeps_running_after_the_source_failed()
    {
        _source.StreamFailure = new InvalidOperationException("the broker connection dropped");
        using var cts = new CancellationTokenSource();
        _source.OnSubscribe = subscriptions =>
        {
            if (subscriptions >= 2)
                cts.Cancel();
        };

        await CreateHost().RunLoopAsync(cts.Token);

        _source.Subscriptions.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_stream_cancelled_without_a_stop_request_does_not_end_the_loop()
    {
        // A broker client's own timeout. Read as a shutdown it would leave the host running with
        // nothing consuming at all.
        _source.StreamCancelsItself = true;
        using var cts = new CancellationTokenSource();
        _source.OnSubscribe = subscriptions =>
        {
            if (subscriptions >= 2)
                cts.Cancel();
        };

        await CreateHost().RunLoopAsync(cts.Token);

        _source.Subscriptions.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_stream_cancelled_by_the_stop_request_ends_the_loop_without_throwing()
    {
        using var cts = new CancellationTokenSource();
        _source.OnSubscribe = _ => cts.Cancel();
        _source.StreamFailure = new OperationCanceledException("the host is stopping", cts.Token);

        var act = async () => await CreateHost().RunLoopAsync(cts.Token);

        await act.Should().NotThrowAsync();
        _source.Subscriptions.Should().Be(1);
    }

    [Fact]
    public async Task A_consumer_that_fails_does_not_take_the_host_down()
    {
        _source.Enqueue(Delivery());
        _consumer.Failure = new InvalidOperationException("the transport refused the acknowledgement");
        using var cts = new CancellationTokenSource();
        _source.OnSubscribe = subscriptions =>
        {
            if (subscriptions >= 2)
                cts.Cancel();
        };

        await CreateHost().RunLoopAsync(cts.Token);

        _source.Subscriptions.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_loop_returns_at_once_when_it_starts_out_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await CreateHost().RunLoopAsync(cts.Token);

        _source.Subscriptions.Should().Be(0);
    }

    [Fact]
    public async Task The_hosted_service_entry_point_runs_the_loop_until_the_host_stops_it()
    {
        _source.Enqueue(Delivery());
        using var consumed = new SemaphoreSlim(0);
        _source.OnSubscribe = subscriptions =>
        {
            if (subscriptions >= 2)
                consumed.Release();
        };
        var host = CreateHost(new ConsumerOptions
        {
            ConsumerName = "orders",
            ReceiveRestartDelay = TimeSpan.FromMilliseconds(10),
        });

        await host.StartAsync(CancellationToken.None);
        var reached = await consumed.WaitAsync(TimeSpan.FromSeconds(10));
        await host.StopAsync(CancellationToken.None);

        reached.Should().BeTrue();
        _consumer.Consumed.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_consumer_without_a_name_is_rejected(string consumerName)
    {
        // It is half of the inbox key, so an empty one silently merges the records of every consumer
        // that also left it empty.
        var act = () => CreateHost(new ConsumerOptions { ConsumerName = consumerName });

        act.Should().Throw<ArgumentException>().WithParameterName("ConsumerName");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_attempt_limit_that_would_park_every_first_delivery_is_rejected(int maxAttempts)
    {
        var act = () => CreateHost(new ConsumerOptions { ConsumerName = "orders", MaxDeliveryAttempts = maxAttempts });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxDeliveryAttempts");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_restart_delay_that_would_spin_the_loop_is_rejected(int delaySeconds)
    {
        var act = () => CreateHost(new ConsumerOptions
        {
            ConsumerName = "orders",
            ReceiveRestartDelay = TimeSpan.FromSeconds(delaySeconds),
        });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ReceiveRestartDelay");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_inbox_ledger_capacity_that_would_record_nothing_is_rejected(int capacity)
    {
        // Startup is where this is cheap to notice: a ledger that evicts every record it takes refuses
        // no redelivery, and the first symptom of that is an event handled twice in production.
        var act = () => CreateHost(new ConsumerOptions
        {
            ConsumerName = "orders",
            InboxLedgerCapacity = capacity,
        });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("InboxLedgerCapacity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_parked_capacity_that_would_keep_no_parked_message_is_rejected(int capacity)
    {
        // A parked message has nothing else holding it, so a bound that keeps none of them makes the
        // loopback drop exactly what parking exists to preserve.
        var act = () => CreateHost(new ConsumerOptions
        {
            ConsumerName = "orders",
            ParkedMessageCapacity = capacity,
        });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ParkedMessageCapacity");
    }

    [Fact]
    public void A_negative_retry_delay_is_rejected()
    {
        // Zero is the documented "hand it back at once", so only a negative one is a mistake — and it
        // would throw from inside the retry path, on the first message that failed.
        var act = () => CreateHost(new ConsumerOptions
        {
            ConsumerName = "orders",
            RetryDelay = TimeSpan.FromMilliseconds(-1),
        });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("RetryDelay");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_inbox_retention_that_would_expire_every_record_at_once_is_rejected(int retentionSeconds)
    {
        // Unset means keep forever and is the default; a retention that is set has to be a duration, or
        // the durable inbox refuses no redelivery at all.
        var act = () => CreateHost(new ConsumerOptions
        {
            ConsumerName = "orders",
            InboxRetention = TimeSpan.FromSeconds(retentionSeconds),
        });

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("InboxRetention");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var options = TestMessagingOptions.For(new ConsumerOptions { ConsumerName = "orders" });
        var logger = NullLogger<IntegrationEventConsumerHost>.Instance;

        var withoutSource = () => new IntegrationEventConsumerHost(null!, _consumer, options, logger);
        var withoutConsumer = () => new IntegrationEventConsumerHost(_source, null!, options, logger);
        var withoutOptions = () => new IntegrationEventConsumerHost(_source, _consumer, null!, logger);
        var withoutLogger = () => new IntegrationEventConsumerHost(_source, _consumer, options, null!);

        withoutSource.Should().Throw<ArgumentNullException>().WithParameterName("source");
        withoutConsumer.Should().Throw<ArgumentNullException>().WithParameterName("consumer");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void The_defaults_bound_attempts_and_back_off_before_re_subscribing()
    {
        var options = new ConsumerOptions();

        options.ConsumerName.Should().BeEmpty();
        options.MaxDeliveryAttempts.Should().Be(10);
        options.ReceiveRestartDelay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_defaults_change_nothing_about_retrying_parking_or_the_durable_inbox()
    {
        // All three are opt-in on purpose: a retry delay stalls the consumer that is retrying, and a
        // retention is the duplicate-detection window, which no default can know.
        var options = new ConsumerOptions();

        options.ParkedMessageCapacity.Should().Be(100);
        options.RetryDelay.Should().Be(TimeSpan.Zero);
        options.InboxRetention.Should().BeNull();
    }
}
