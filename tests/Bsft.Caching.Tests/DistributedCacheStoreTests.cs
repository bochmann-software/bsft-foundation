using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Caching.Tests;

public class DistributedCacheStoreTests
{
    private static IDistributedCache CreateCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static DistributedCacheStore CreateStore(IDistributedCache? cache = null) =>
        new(cache ?? CreateCache(), new JsonCacheSerializer(), NullLogger<DistributedCacheStore>.Instance);

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new DistributedCacheStore(null!, new JsonCacheSerializer(), NullLogger<DistributedCacheStore>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullSerializer_Throws()
    {
        var act = () => new DistributedCacheStore(CreateCache(), null!, NullLogger<DistributedCacheStore>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DistributedCacheStore(CreateCache(), new JsonCacheSerializer(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsAnEquivalentValue()
    {
        var store = CreateStore();
        await store.SetAsync("key", new Person("Ada"), TimeSpan.FromMinutes(1));

        var result = await store.GetAsync<Person>("key");

        result.IsHit.Should().BeTrue();
        result.Value.Should().Be(new Person("Ada"));
    }

    [Fact]
    public async Task GetAsync_UnknownKey_IsAMiss()
    {
        var result = await CreateStore().GetAsync<Person>("absent");

        result.IsHit.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_PayloadDeserializingToNull_IsAMiss()
    {
        var cache = CreateCache();
        await cache.SetStringAsync("key", "null");

        var result = await CreateStore(cache).GetAsync<Person>("key");

        result.IsHit.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_UnreadablePayload_IsAMissAndDropsTheEntry()
    {
        var cache = CreateCache();
        await cache.SetStringAsync("key", "not json");

        var result = await CreateStore(cache).GetAsync<Person>("key");

        result.IsHit.Should().BeFalse();
        (await cache.GetAsync("key")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_EvictsTheEntry()
    {
        var store = CreateStore();
        await store.SetAsync("key", new Person("Ada"), TimeSpan.FromMinutes(1));

        await store.RemoveAsync("key");

        (await store.GetAsync<Person>("key")).IsHit.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsync_AppliesTheLifetimeAsAbsoluteExpiration()
    {
        var cache = Substitute.For<IDistributedCache>();

        await CreateStore(cache).SetAsync("key", new Person("Ada"), TimeSpan.FromMinutes(3));

        await cache.Received(1).SetAsync(
            "key",
            Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(3)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_NullKey_Throws()
    {
        var act = async () => await CreateStore().GetAsync<Person>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetAsync_NullKey_Throws()
    {
        var act = async () => await CreateStore().SetAsync(null!, new Person("Ada"), TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveAsync_NullKey_Throws()
    {
        var act = async () => await CreateStore().RemoveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
