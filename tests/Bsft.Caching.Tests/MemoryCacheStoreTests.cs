using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Bsft.Caching.Tests;

public class MemoryCacheStoreTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new MemoryCacheStore(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsTheValue()
    {
        var store = new MemoryCacheStore(_cache);
        await store.SetAsync("key", new Person("Ada"), TimeSpan.FromMinutes(1));

        var result = await store.GetAsync<Person>("key");

        result.IsHit.Should().BeTrue();
        result.Value.Should().Be(new Person("Ada"));
    }

    [Fact]
    public async Task GetAsync_CachedDefaultValue_IsStillAHit()
    {
        var store = new MemoryCacheStore(_cache);
        await store.SetAsync("key", 0, TimeSpan.FromMinutes(1));

        var result = await store.GetAsync<int>("key");

        result.IsHit.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_UnknownKey_IsAMiss()
    {
        var result = await new MemoryCacheStore(_cache).GetAsync<Person>("absent");

        result.IsHit.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_KeyHoldingAnotherType_IsAMiss()
    {
        var store = new MemoryCacheStore(_cache);
        await store.SetAsync("key", new Person("Ada"), TimeSpan.FromMinutes(1));

        var result = await store.GetAsync<string>("key");

        result.IsHit.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_EvictsTheEntry()
    {
        var store = new MemoryCacheStore(_cache);
        await store.SetAsync("key", new Person("Ada"), TimeSpan.FromMinutes(1));

        await store.RemoveAsync("key");

        (await store.GetAsync<Person>("key")).IsHit.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_UnknownKey_DoesNotThrow()
    {
        var act = async () => await new MemoryCacheStore(_cache).RemoveAsync("absent");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAsync_NullKey_Throws()
    {
        var act = async () => await new MemoryCacheStore(_cache).GetAsync<Person>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SetAsync_NullKey_Throws()
    {
        var act = async () => await new MemoryCacheStore(_cache).SetAsync(null!, new Person("Ada"), TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RemoveAsync_NullKey_Throws()
    {
        var act = async () => await new MemoryCacheStore(_cache).RemoveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
