using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

public class SagaStateEntityTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var entity = new SagaStateEntity();

        entity.Id.Should().Be(0);
        entity.SagaType.Should().Be(string.Empty);
        entity.CorrelationKey.Should().Be(string.Empty);
        entity.StateTypeName.Should().Be(string.Empty);
        entity.StateJson.Should().Be(string.Empty);
        entity.CurrentStepIndex.Should().Be(0);
        entity.IsComplete.Should().BeFalse();
        entity.CreatedAt.Should().Be(default);
        entity.UpdatedAt.Should().Be(default);
    }

    [Fact]
    public void Properties_CanBeSetAndGet()
    {
        var now = DateTime.UtcNow;
        var entity = new SagaStateEntity
        {
            Id = 42,
            SagaType = "OrderSaga",
            CorrelationKey = "order-1",
            StateTypeName = "OrderState",
            StateJson = "{\"paid\":true}",
            CurrentStepIndex = 3,
            IsComplete = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        entity.Id.Should().Be(42);
        entity.SagaType.Should().Be("OrderSaga");
        entity.CorrelationKey.Should().Be("order-1");
        entity.StateTypeName.Should().Be("OrderState");
        entity.StateJson.Should().Be("{\"paid\":true}");
        entity.CurrentStepIndex.Should().Be(3);
        entity.IsComplete.Should().BeTrue();
        entity.CreatedAt.Should().Be(now);
        entity.UpdatedAt.Should().Be(now);
    }
}
