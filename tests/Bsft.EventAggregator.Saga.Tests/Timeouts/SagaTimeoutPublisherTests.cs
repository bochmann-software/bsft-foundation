using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

public class SagaTimeoutPublisherTests
{
    private readonly IEventAggregator _aggregator = new SimpleEventAggregator();
    private readonly ISagaCorrelationScope _correlationScope = new SagaCorrelationScope();

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        FluentActions.Invoking(() => new SagaTimeoutPublisher(null!, _correlationScope))
            .Should().Throw<ArgumentNullException>().WithParameterName("eventAggregator");
        FluentActions.Invoking(() => new SagaTimeoutPublisher(_aggregator, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("correlationScope");
    }

    [Fact]
    public async Task PublishAsync_WithoutATimeout_ThrowsArgumentNullException()
    {
        var publisher = new SagaTimeoutPublisher(_aggregator, _correlationScope);

        var act = () => publisher.PublishAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("timeout");
    }
}
