using Bsft.Caching.Invalidation;
using Bsft.EventAggregator;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.Caching.EventAggregator.Tests;

public class CacheInvalidationSubscriberTests
{
    private readonly IEventAggregator _eventAggregator = new SimpleEventAggregator();
    private readonly ICacheInvalidator _invalidator = Substitute.For<ICacheInvalidator>();

    private CacheInvalidationSubscriber CreateSubscriber() => new(_eventAggregator, _invalidator);

    [Fact]
    public void Constructor_NullEventAggregator_Throws()
    {
        var act = () => new CacheInvalidationSubscriber(null!, _invalidator);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullInvalidator_Throws()
    {
        var act = () => new CacheInvalidationSubscriber(_eventAggregator, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task KeyEvent_AfterStart_InvalidatesTheKey()
    {
        await CreateSubscriber().StartAsync(CancellationToken.None);

        await _eventAggregator.PublishAsync(new CacheKeyInvalidatedEvent("bsft:Query:1"));

        await _invalidator.Received(1).InvalidateKeyAsync("bsft:Query:1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TagEvent_AfterStart_InvalidatesTheTag()
    {
        await CreateSubscriber().StartAsync(CancellationToken.None);

        await _eventAggregator.PublishAsync(new CacheTagInvalidatedEvent("orders"));

        await _invalidator.Received(1).InvalidateTagAsync("orders", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Events_BeforeStart_AreIgnored()
    {
        CreateSubscriber();

        await _eventAggregator.PublishAsync(new CacheTagInvalidatedEvent("orders"));

        await _invalidator.DidNotReceive().InvalidateTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Events_AfterStop_AreIgnored()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        await subscriber.StopAsync(CancellationToken.None);
        await _eventAggregator.PublishAsync(new CacheKeyInvalidatedEvent("bsft:Query:1"));
        await _eventAggregator.PublishAsync(new CacheTagInvalidatedEvent("orders"));

        await _invalidator.DidNotReceive().InvalidateKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _invalidator.DidNotReceive().InvalidateTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Events_CarryTheirPayload()
    {
        new CacheKeyInvalidatedEvent("bsft:Query:1").Key.Should().Be("bsft:Query:1");
        new CacheTagInvalidatedEvent("orders").Tag.Should().Be("orders");
    }
}
