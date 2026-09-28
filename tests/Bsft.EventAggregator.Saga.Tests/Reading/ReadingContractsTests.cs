using Bsft.EventAggregator.Saga.Reading;
using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Reading;

/// <summary>
/// The shapes an operator reads running sagas through.
/// </summary>
public sealed class ReadingContractsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A filter with nothing set narrows nothing, which is what a bare listing sends.</summary>
    [Fact]
    public void SagaInstanceFilter_WithoutCriteria_IsEmpty()
    {
        var filter = new SagaInstanceFilter();

        filter.SagaType.Should().BeNull();
        filter.SagaTypePrefix.Should().BeNull();
        filter.CorrelationKey.Should().BeNull();
        filter.StateTypeName.Should().BeNull();
        filter.IsComplete.Should().BeNull();
        filter.CreatedAfter.Should().BeNull();
        filter.CreatedBefore.Should().BeNull();
        filter.UpdatedAfter.Should().BeNull();
        filter.UpdatedBefore.Should().BeNull();
        filter.MinStepIndex.Should().BeNull();
    }

    /// <summary>Every criterion is carried as it was given.</summary>
    [Fact]
    public void SagaInstanceFilter_WithCriteria_CarriesThem()
    {
        var filter = new SagaInstanceFilter(
            "Orders.Fulfil", "Orders.", "order-1", "FulfilState", false, Now, Now, Now, Now, 2);

        filter.Should().Be(new SagaInstanceFilter(
            "Orders.Fulfil", "Orders.", "order-1", "FulfilState", false, Now, Now, Now, Now, 2));
        filter.MinStepIndex.Should().Be(2);
    }

    /// <summary>A listing row carries the progress and, deliberately, no state.</summary>
    [Fact]
    public void SagaInstanceSummary_Always_CarriesTheProgress()
    {
        var summary = new SagaInstanceSummary(
            7, "Orders.Fulfil", "order-1", "FulfilState", 3, true, Now.AddHours(-1), Now);

        summary.Id.Should().Be(7);
        summary.SagaType.Should().Be("Orders.Fulfil");
        summary.CorrelationKey.Should().Be("order-1");
        summary.StateTypeName.Should().Be("FulfilState");
        summary.CurrentStepIndex.Should().Be(3);
        summary.IsComplete.Should().BeTrue();
        summary.CreatedAt.Should().Be(Now.AddHours(-1));
        summary.UpdatedAt.Should().Be(Now);
    }

    /// <summary>The state is what the second read adds to the first.</summary>
    [Fact]
    public void SagaInstanceView_Always_AddsTheStateToTheSummary()
    {
        var summary = new SagaInstanceSummary(7, "Orders.Fulfil", "order-1", "FulfilState", 0, false, Now, Now);

        var view = new SagaInstanceView(summary, """{"paid":true}""");

        view.Summary.Should().BeSameAs(summary);
        view.StateJson.Should().Be("""{"paid":true}""");
    }

    /// <summary>A timeout carries what it will publish and what retired it.</summary>
    [Fact]
    public void SagaTimeoutView_Always_CarriesWhatTheSagaIsWaitingFor()
    {
        var timeout = new SagaTimeoutView(
            11, "reminder", 3, Now.AddHours(1), "Orders.Reminder, Orders", """{"orderId":7}""", "corr-1", 5,
            Now, Now, "handler threw");

        timeout.Id.Should().Be(11);
        timeout.Name.Should().Be("reminder");
        timeout.StepIndex.Should().Be(3);
        timeout.DueAt.Should().Be(Now.AddHours(1));
        timeout.PayloadTypeName.Should().Be("Orders.Reminder, Orders");
        timeout.PayloadJson.Should().Be("""{"orderId":7}""");
        timeout.CorrelationId.Should().Be("corr-1");
        timeout.AttemptCount.Should().Be(5);
        timeout.DispatchedAt.Should().Be(Now);
        timeout.DeadLetteredAt.Should().Be(Now);
        timeout.DeadLetterReason.Should().Be("handler threw");
    }
}
