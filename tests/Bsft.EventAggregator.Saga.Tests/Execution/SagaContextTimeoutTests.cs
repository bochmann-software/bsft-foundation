using Bsft.EventAggregator.Saga.Execution;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Execution;

public class SagaContextTimeoutTests : IDisposable
{
    public record QuoteFollowUpDue(string QuoteId);

    public class QuoteState;

    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void ScheduleTimeout_WithAnAbsoluteDueDate_IsRecordedWithoutADelay()
    {
        var context = CreateContext();
        var due = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);

        context.ScheduleTimeout("followup", due, new QuoteFollowUpDue("q-1"));

        var recorded = context.PendingTimeouts.Should().ContainSingle().Subject;
        recorded.Kind.Should().Be(PendingSagaTimeoutKind.Schedule);
        recorded.DueAt.Should().Be(due);
        recorded.Delay.Should().BeNull();
        recorded.Name.Should().Be("followup");
    }

    [Fact]
    public void ScheduleTimeout_WithADelay_KeepsTheDelayAndTheCalendarKey()
    {
        var context = CreateContext();

        context.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue("q-1"), "berlin");

        var recorded = context.PendingTimeouts.Should().ContainSingle().Subject;
        recorded.Delay.Should().Be(TimeSpan.FromDays(2));
        recorded.CalendarKey.Should().Be("berlin");
        recorded.DueAt.Should().BeNull();
    }

    [Fact]
    public void CancelTimeout_AndCancelAllTimeouts_AreRecordedInOrder()
    {
        var context = CreateContext();

        context.CancelTimeout("followup");
        context.CancelAllTimeouts();

        context.PendingTimeouts.Select(t => t.Kind).Should()
            .Equal(PendingSagaTimeoutKind.Cancel, PendingSagaTimeoutKind.CancelAll);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyTimeoutName_IsRejected(string? name)
    {
        var context = CreateContext();

        FluentActions.Invoking(() => context.ScheduleTimeout(name!, TimeSpan.FromDays(1), new QuoteFollowUpDue("q-1")))
            .Should().Throw<ArgumentException>().WithParameterName("name");
        FluentActions.Invoking(() => context.ScheduleTimeout(name!, DateTimeOffset.UtcNow, new QuoteFollowUpDue("q-1")))
            .Should().Throw<ArgumentException>().WithParameterName("name");
        FluentActions.Invoking(() => context.CancelTimeout(name!))
            .Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void ANullTimeoutEvent_IsRejected()
    {
        var context = CreateContext();

        FluentActions.Invoking(() => context.ScheduleTimeout<QuoteFollowUpDue>("followup", TimeSpan.FromDays(1), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeoutEvent");
        FluentActions.Invoking(() => context.ScheduleTimeout<QuoteFollowUpDue>("followup", DateTimeOffset.UtcNow, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeoutEvent");
    }

    [Fact]
    public void WithoutACorrelationAccessor_NoCorrelationIdIsCaptured()
    {
        CreateContext().PendingTimeouts.Should().BeEmpty();

        var context = CreateContext();
        context.CancelTimeout("followup");

        context.PendingTimeouts[0].CorrelationId.Should().BeNull();
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private SagaContext<QuoteState> CreateContext() =>
        new(new SagaInstance<QuoteState>("Sales", "q-1", new QuoteState()), new SimpleEventAggregator(), _services);
}
