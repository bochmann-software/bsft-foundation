using Bsft.EventAggregator.Saga.Execution;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Execution;

public class SagaContextTests
{
    public class MyState
    {
        public int Value { get; set; }
    }

    public interface IMyService
    {
        int GetValue();
    }

    [Fact]
    public void CorrelationKey_ReturnsInstanceCorrelationKey()
    {
        var ea = Substitute.For<IEventAggregator>();
        var services = new ServiceCollection().BuildServiceProvider();
        var instance = new SagaInstance<MyState>("MySaga", "corr-key-1", new MyState());
        var ctx = new SagaContext<MyState>(instance, ea, services);

        ctx.CorrelationKey.Should().Be("corr-key-1");
    }

    [Fact]
    public void Services_ReturnsServiceProvider()
    {
        var ea = Substitute.For<IEventAggregator>();
        var services = new ServiceCollection().BuildServiceProvider();
        var instance = new SagaInstance<MyState>("MySaga", "key", new MyState());
        var ctx = new SagaContext<MyState>(instance, ea, services);

        ctx.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void Resolve_ReturnsRegisteredService()
    {
        var ea = Substitute.For<IEventAggregator>();
        var myService = Substitute.For<IMyService>();
        myService.GetValue().Returns(42);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(myService);
        var services = serviceCollection.BuildServiceProvider();

        var instance = new SagaInstance<MyState>("MySaga", "key", new MyState());
        var ctx = new SagaContext<MyState>(instance, ea, services);

        var resolved = ctx.GetService<IMyService>();
        resolved.Should().BeSameAs(myService);
        resolved.GetValue().Should().Be(42);
    }

    [Fact]
    public async Task PublishAsync_PublishesToEventAggregator()
    {
        var ea = Substitute.For<IEventAggregator>();
        var services = new ServiceCollection().BuildServiceProvider();
        var instance = new SagaInstance<MyState>("MySaga", "key", new MyState());
        var ctx = new SagaContext<MyState>(instance, ea, services);

        var @event = new object();
        await ctx.PublishAsync(@event);

        await ea.Received(1).PublishAsync(@event, CancellationToken.None);
    }

    [Fact]
    public void Constructor_NullInstance_ThrowsArgumentNullException()
    {
        var ea = Substitute.For<IEventAggregator>();
        var services = new ServiceCollection().BuildServiceProvider();

        var act = () => new SagaContext<MyState>(null!, ea, services);

        act.Should().Throw<ArgumentNullException>().WithParameterName("instance");
    }

    [Fact]
    public void Constructor_NullEventAggregator_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var instance = new SagaInstance<MyState>("MySaga", "key", new MyState());

        var act = () => new SagaContext<MyState>(instance, null!, services);

        act.Should().Throw<ArgumentNullException>().WithParameterName("eventAggregator");
    }

    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        var ea = Substitute.For<IEventAggregator>();
        var instance = new SagaInstance<MyState>("MySaga", "key", new MyState());

        var act = () => new SagaContext<MyState>(instance, ea, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void MarkComplete_SetsInstanceIsCompleteToTrue()
    {
        // Exercises SagaContext.MarkComplete lines 48-50: sets _instance.IsComplete = true.
        var ea = Substitute.For<IEventAggregator>();
        var services = new ServiceCollection().BuildServiceProvider();
        var instance = new SagaInstance<MyState>("MySaga", "key", new MyState());
        var ctx = new SagaContext<MyState>(instance, ea, services);

        ctx.MarkComplete();

        instance.IsComplete.Should().BeTrue();
    }
}
