using Bsft.Caching.Invalidation;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Caching.Tests.Invalidation;

public class CacheInvalidatorTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TestTimeProvider _time = new();
    private readonly BsftCachingOptions _options = new();
    private readonly CacheTagIndex _index;

    public CacheInvalidatorTests()
    {
        _index = new CacheTagIndex(_time);
    }

    private ICacheStore CreateStore() => new MemoryCacheStore(_cache);

    private CacheInvalidator CreateInvalidator(ICacheStore? store = null) =>
        new(store ?? CreateStore(), _index, Options.Create(_options));

    private DateTimeOffset InAnHour() => _time.GetUtcNow() + TimeSpan.FromHours(1);

    /// <summary>
    /// A store that lets a query re-cache <paramref name="recachedKey"/> the moment its eviction
    /// lands, which is the window between the invalidator's snapshot and its forget.
    /// </summary>
    private ICacheStore CreateRecachingStore(string recachedKey)
    {
        var inner = CreateStore();

        return new RecachingCacheStore(inner, async key =>
        {
            if (key != recachedKey)
                return;

            await inner.SetAsync(key, new Person("re-cached"), TimeSpan.FromMinutes(1));
            _index.Associate(key, ["orders"], InAnHour());
        });
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new CacheInvalidator(null!, _index, Options.Create(_options));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTagIndex_Throws()
    {
        var act = () => new CacheInvalidator(CreateStore(), null!, Options.Create(_options));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new CacheInvalidator(CreateStore(), _index, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InvalidateKeyAsync_EvictsTheEntryAndForgetsItsTags()
    {
        var store = CreateStore();
        await store.SetAsync("bsft:key", new Person("Ada"), TimeSpan.FromMinutes(1));
        _index.Associate("bsft:key", ["orders"], InAnHour());

        await CreateInvalidator(store).InvalidateKeyAsync("bsft:key");

        (await store.GetAsync<Person>("bsft:key")).IsHit.Should().BeFalse();
        _index.ListKeys("orders").Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateKeyAsync_ForeignKey_Throws()
    {
        var act = async () => await CreateInvalidator().InvalidateKeyAsync("other-service:key");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*prefix*");
    }

    [Fact]
    public async Task InvalidateTagAsync_EvictsEveryEntryCarryingTheTag()
    {
        var store = CreateStore();
        await store.SetAsync("bsft:key-1", new Person("Ada"), TimeSpan.FromMinutes(1));
        await store.SetAsync("bsft:key-2", new Person("Grace"), TimeSpan.FromMinutes(1));
        await store.SetAsync("bsft:key-3", new Person("Alan"), TimeSpan.FromMinutes(1));
        _index.Associate("bsft:key-1", ["orders"], InAnHour());
        _index.Associate("bsft:key-2", ["orders"], InAnHour());
        _index.Associate("bsft:key-3", ["customers"], InAnHour());

        await CreateInvalidator(store).InvalidateTagAsync("orders");

        (await store.GetAsync<Person>("bsft:key-1")).IsHit.Should().BeFalse();
        (await store.GetAsync<Person>("bsft:key-2")).IsHit.Should().BeFalse();
        (await store.GetAsync<Person>("bsft:key-3")).IsHit.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateTagAsync_KeyCarryingSeveralTags_IsForgottenEverywhere()
    {
        var store = CreateStore();
        await store.SetAsync("bsft:key", new Person("Ada"), TimeSpan.FromMinutes(1));
        _index.Associate("bsft:key", ["orders", "customers"], InAnHour());

        await CreateInvalidator(store).InvalidateTagAsync("orders");

        _index.ListKeys("customers").Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateTagAsync_MoreKeysThanOneEvictionBatch_EvictsAllOfThem()
    {
        var store = CreateStore();
        var keys = Enumerable.Range(0, 70).Select(i => $"bsft:key-{i}").ToList();

        foreach (var key in keys)
        {
            await store.SetAsync(key, new Person(key), TimeSpan.FromMinutes(1));
            _index.Associate(key, ["orders"], InAnHour());
        }

        await CreateInvalidator(store).InvalidateTagAsync("orders");

        foreach (var key in keys)
            (await store.GetAsync<Person>(key)).IsHit.Should().BeFalse();

        _index.ListKeys("orders").Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateTagAsync_UnknownTag_DoesNothing()
    {
        var store = CreateStore();
        await store.SetAsync("bsft:key", new Person("Ada"), TimeSpan.FromMinutes(1));

        await CreateInvalidator(store).InvalidateTagAsync("unknown");

        (await store.GetAsync<Person>("bsft:key")).IsHit.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateKeyAsync_EntryRecachedDuringTheEviction_KeepsTheFreshAssociation()
    {
        // Regression: a query re-caching the key between the eviction and the forget re-associates
        // it. Forgetting unconditionally deleted that association while the re-cached entry stayed
        // in the store, out of reach of every later tag invalidation for its whole lifetime.
        var store = CreateRecachingStore("bsft:key");
        await store.SetAsync("bsft:key", new Person("Ada"), TimeSpan.FromMinutes(1));
        _index.Associate("bsft:key", ["orders"], InAnHour());

        await CreateInvalidator(store).InvalidateKeyAsync("bsft:key");

        (await store.GetAsync<Person>("bsft:key")).IsHit.Should().BeTrue("the concurrent query re-cached it");
        _index.ListKeys("orders").Should().BeEquivalentTo(["bsft:key"]);
    }

    [Fact]
    public async Task InvalidateTagAsync_EntryRecachedDuringTheEviction_KeepsTheFreshAssociation()
    {
        var store = CreateRecachingStore("bsft:key-1");
        await store.SetAsync("bsft:key-1", new Person("Ada"), TimeSpan.FromMinutes(1));
        await store.SetAsync("bsft:key-2", new Person("Grace"), TimeSpan.FromMinutes(1));
        _index.Associate("bsft:key-1", ["orders"], InAnHour());
        _index.Associate("bsft:key-2", ["orders"], InAnHour());

        await CreateInvalidator(store).InvalidateTagAsync("orders");

        (await store.GetAsync<Person>("bsft:key-2")).IsHit.Should().BeFalse("nothing re-cached it");
        _index.ListKeys("orders").Should().BeEquivalentTo(["bsft:key-1"]);
    }

    [Fact]
    public async Task InvalidateKeyAsync_NullKey_DoesNothing()
    {
        // Build returns no key for a scope that is not cacheable. A best-effort invalidation passing
        // that result through must not fail the write it follows: nothing was stored under it.
        var store = Substitute.For<ICacheStore>();

        var act = async () => await CreateInvalidator(store).InvalidateKeyAsync(null);

        await act.Should().NotThrowAsync();
        await store.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTagAsync_NullTag_Throws()
    {
        var act = async () => await CreateInvalidator().InvalidateTagAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
