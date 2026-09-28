using Bsft.EventAggregator.Saga;
using Bsft.EventAggregator;
using Bsft.Testing.Contexts;
using Bsft.Testing.Sagas;
using Bsft.Testing.Tests.Support;
using Bsft.Testing.Time;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Bsft.EventAggregator.Saga.Timeouts;

namespace Bsft.Testing.Tests.Sagas;

public class SagaHarnessTests
{
    private static ISagaTestHarness BuildOrderHarness(ITrace trace) =>
        new SagaHarnessBuilder()
            .WithService<ITrace>(trace)
            .WithFlow<OrderFlow>()
            .Build();

    [Fact]
    public async Task Starting_event_creates_the_saga_and_schedules_its_timer()
    {
        using var harness = BuildOrderHarness(new Trace());

        await harness.PublishAsync(new OrderPlaced("order-1"));

        var saga = await harness.FindSagaAsync<OrderState, OrderPlaced>("order-1");
        saga.Should().NotBeNull();
        var pending = await harness.ListPendingTimeoutsAsync();
        pending.Should().ContainSingle().Which.Name.Should().Be("reminder");
    }

    [Fact]
    public async Task Timer_stays_pending_while_it_is_not_due_yet()
    {
        using var harness = BuildOrderHarness(new Trace());
        await harness.PublishAsync(new OrderPlaced("order-1"));

        var fired = await harness.AdvanceTimeAsync(TimeSpan.FromDays(1));

        fired.Should().Be(0);
        (await harness.ListPendingTimeoutsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Advancing_past_the_due_date_fires_the_timer_into_the_saga()
    {
        var trace = new Trace();
        using var harness = BuildOrderHarness(trace);
        await harness.PublishAsync(new OrderPlaced("order-1"));

        var fired = await harness.AdvanceTimeAsync(TimeSpan.FromDays(3));

        fired.Should().Be(1);
        trace.Entries.Should().ContainSingle().Which.Should().Be("reminder-fired");
        var saga = await harness.FindSagaAsync<OrderState, OrderPlaced>("order-1");
        saga!.State.Reminders.Should().Be(1);
        (await harness.ListPendingTimeoutsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Completed_saga_is_gone_from_the_store()
    {
        using var harness = BuildOrderHarness(new Trace());
        await harness.PublishAsync(new OrderPlaced("order-1"));
        await harness.AdvanceTimeAsync(TimeSpan.FromDays(3));

        await harness.PublishAsync(new OrderPaid("order-1"));

        (await harness.FindSagaAsync<OrderState, OrderPlaced>("order-1")).Should().BeNull();
    }

    [Fact]
    public async Task Draining_without_moving_the_clock_fires_nothing_that_is_not_due()
    {
        using var harness = BuildOrderHarness(new Trace());
        await harness.PublishAsync(new OrderPlaced("order-1"));

        (await harness.DrainTimeoutsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Drain_empties_a_backlog_larger_than_one_batch()
    {
        using var harness = new SagaHarnessBuilder()
            .WithFlow<SurveyFlow>()
            .ConfigureEngine(options => options.Timeouts.BatchSize = 1)
            .Build();
        await harness.PublishAsync(new SurveySent("survey-1"));
        await harness.PublishAsync(new SurveySent("survey-2"));

        var fired = await harness.AdvanceTimeAsync(TimeSpan.FromDays(2));

        fired.Should().Be(2);
        (await harness.ListPendingTimeoutsAsync()).Should().BeEmpty();
        (await harness.FindSagaAsync<SurveyState, SurveySent>("survey-2"))!.State.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_whose_step_fails_stays_pending_instead_of_spinning()
    {
        using var harness = new SagaHarnessBuilder()
            .WithFlow<FailingTimeoutFlow>()
            .Build();
        await harness.PublishAsync(new ShipmentDispatched("shipment-1"));

        var fired = await harness.AdvanceTimeAsync(TimeSpan.FromDays(2));

        fired.Should().Be(0);
        (await harness.ListPendingTimeoutsAsync()).Should().ContainSingle().Which.Name.Should().Be("overdue");
    }

    [Fact]
    public async Task Clock_starts_where_the_test_put_it()
    {
        var start = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        using var harness = new SagaHarnessBuilder()
            .WithFlow<SurveyFlow>()
            .StartingAt(start)
            .Build();

        await harness.PublishAsync(new SurveySent("survey-1"));

        harness.Clock.UtcNow.Should().Be(start);
        var pending = await harness.ListPendingTimeoutsAsync();
        pending.Should().ContainSingle().Which.DueAt.Should().Be(start.AddDays(1));
    }

    [Fact]
    public void Saga_type_is_read_back_from_the_registered_definition()
    {
        using var harness = BuildOrderHarness(new Trace());

        harness.GetSagaType<OrderState, OrderPlaced>().Should().Be("OrderState_OrderPlaced");
    }

    [Fact]
    public void Unregistered_flow_is_reported_rather_than_guessed()
    {
        using var harness = BuildOrderHarness(new Trace());

        var read = () => harness.GetSagaType<SurveyState, SurveySent>();

        read.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_correlation_key_is_rejected(string? correlationKey)
    {
        using var harness = BuildOrderHarness(new Trace());

        var read = async () => await harness.FindSagaAsync<OrderState, OrderPlaced>(correlationKey!);

        await read.Should().ThrowAsync<ArgumentException>().WithParameterName("correlationKey");
    }

    [Fact]
    public async Task Test_contexts_are_available_to_a_saga_step()
    {
        using var harness = new SagaHarnessBuilder()
            .WithTestContexts()
            .WithFlow<SurveyFlow>()
            .Build();

        await harness.PublishAsync(new SurveySent("survey-1"));

        harness.Services.GetRequiredService<TestTenantContext>().TenantId.Should().Be(TestTenantContext.DefaultTenantId);
        harness.Services.GetRequiredService<TestCorrelationContext>().CorrelationId
            .Should().Be(TestCorrelationContext.DefaultCorrelationId);
    }

    [Fact]
    public async Task Extra_registrations_reach_the_container()
    {
        using var harness = new SagaHarnessBuilder()
            .WithServices(services => services.AddSingleton<ITrace, Trace>())
            .WithFlow<OrderFlow>()
            .Build();
        await harness.PublishAsync(new OrderPlaced("order-1"));

        await harness.AdvanceTimeAsync(TimeSpan.FromDays(3));

        harness.Services.GetRequiredService<ITrace>().Entries.Should().ContainSingle();
    }

    [Fact]
    public void Stores_and_aggregator_are_exposed_for_direct_assertions()
    {
        using var harness = BuildOrderHarness(new Trace());

        harness.Events.Should().BeSameAs(harness.Services.GetRequiredService<IEventAggregator>());
        harness.Store.Should().BeSameAs(harness.Services.GetRequiredService<ISagaStore>());
        harness.Timeouts.Should().BeSameAs(
            harness.Services.GetRequiredService<Bsft.EventAggregator.Saga.Timeouts.ISagaTimeoutStore>());
    }

    [Fact]
    public void Disposing_the_harness_disposes_the_container()
    {
        var harness = BuildOrderHarness(new Trace());
        var services = harness.Services;

        harness.Dispose();

        var resolve = () => services.GetRequiredService<ISagaStore>();
        resolve.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Harness_rejects_missing_arguments()
    {
        using var built = BuildOrderHarness(new Trace());

        var withoutServices = () => new SagaTestHarness(null!, new TestClock());
        var withoutClock = () => new SagaTestHarness(built.Services, null!);

        withoutServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        withoutClock.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [Fact]
    public void Builder_rejects_null_arguments()
    {
        var builder = new SagaHarnessBuilder();

        var calls = new List<Action>
        {
            () => builder.WithService<ITrace>(null!),
            () => builder.WithServices(null!),
            () => builder.ConfigureEngine(null!),
        };

        calls.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
