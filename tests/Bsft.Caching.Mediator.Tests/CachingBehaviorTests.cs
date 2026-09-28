using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Bsft.Caching.Invalidation;
using Bsft.Caching.Keys;

namespace Bsft.Caching.Mediator.Tests;

public class CachingBehaviorTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TestTimeProvider _time = new();
    private readonly BsftCachingOptions _options = new();
    private readonly CacheTagIndex _index;

    public CachingBehaviorTests()
    {
        _index = new CacheTagIndex(_time);
    }

    private CacheKeyBuilder KeyBuilder => new(Options.Create(_options), new GlobalCacheKeyScopeProvider());

    private static ILogger<CachingBehavior<TRequest, string>> Logger<TRequest>() =>
        NullLogger<CachingBehavior<TRequest, string>>.Instance;

    private CachingBehavior<TRequest, string> CreateBehavior<TRequest>(
        ICacheStore? store = null,
        ICacheKeyScopeProvider? scopeProvider = null)
    {
        var keyBuilder = scopeProvider is null
            ? KeyBuilder
            : new CacheKeyBuilder(Options.Create(_options), scopeProvider);

        return new CachingBehavior<TRequest, string>(
            store ?? new MemoryCacheStore(_cache), keyBuilder, _index, Options.Create(_options), _time, Logger<TRequest>());
    }

    /// <summary>A scope provider whose scope the test changes between calls.</summary>
    private sealed class MutableScopeProvider : ICacheKeyScopeProvider
    {
        public MutableScopeProvider(CacheKeyScope scope)
        {
            Scope = scope;
        }

        public CacheKeyScope Scope { get; set; }

        public CacheKeyScope GetScope() => Scope;
    }

    private static ICacheStore CreateMissingStore()
    {
        var store = Substitute.For<ICacheStore>();
        store.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheLookupResult<string>(false, null));

        return store;
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new CachingBehavior<DefaultsQuery, string>(null!, KeyBuilder, _index, Options.Create(_options), _time, Logger<DefaultsQuery>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullKeyBuilder_Throws()
    {
        var act = () => new CachingBehavior<DefaultsQuery, string>(new MemoryCacheStore(_cache), null!, _index, Options.Create(_options), _time, Logger<DefaultsQuery>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTagIndex_Throws()
    {
        var act = () => new CachingBehavior<DefaultsQuery, string>(new MemoryCacheStore(_cache), KeyBuilder, null!, Options.Create(_options), _time, Logger<DefaultsQuery>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new CachingBehavior<DefaultsQuery, string>(new MemoryCacheStore(_cache), KeyBuilder, _index, null!, _time, Logger<DefaultsQuery>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        var act = () => new CachingBehavior<DefaultsQuery, string>(new MemoryCacheStore(_cache), KeyBuilder, _index, Options.Create(_options), null!, Logger<DefaultsQuery>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new CachingBehavior<DefaultsQuery, string>(new MemoryCacheStore(_cache), KeyBuilder, _index, Options.Create(_options), _time, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_NullNext_Throws()
    {
        var act = async () => await CreateBehavior<DefaultsQuery>().HandleAsync(new DefaultsQuery("k"), null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutTheMarker_IsNotCached()
    {
        var calls = 0;
        var behavior = CreateBehavior<PlainQuery>();
        var request = new PlainQuery("v");

        Task<string> Next()
        {
            calls++;
            return Task.FromResult("result");
        }

        (await behavior.HandleAsync(request, Next)).Should().Be("result");
        (await behavior.HandleAsync(request, Next)).Should().Be("result");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_CacheableCommand_IsNeverServedFromTheCache()
    {
        var calls = 0;
        var behavior = CreateBehavior<CacheableCommand>();
        var request = new CacheableCommand("k");

        Task<string> Next()
        {
            calls++;
            return Task.FromResult($"result-{calls}");
        }

        (await behavior.HandleAsync(request, Next)).Should().Be("result-1");
        (await behavior.HandleAsync(request, Next)).Should().Be("result-2");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_RequestThatIsBothCommandAndQuery_IsNeverServedFromTheCache()
    {
        var calls = 0;
        var behavior = CreateBehavior<CacheableCommandAndQuery>();
        var request = new CacheableCommandAndQuery("k");

        Task<string> Next()
        {
            calls++;
            return Task.FromResult($"result-{calls}");
        }

        (await behavior.HandleAsync(request, Next)).Should().Be("result-1");
        (await behavior.HandleAsync(request, Next)).Should().Be("result-2");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WhenTheScopeIsNotCacheable_RunsTheHandlerEveryTime()
    {
        var calls = 0;
        var behavior = CreateBehavior<DefaultsQuery>(scopeProvider: new MutableScopeProvider(CacheKeyScope.NotCacheable));
        var request = new DefaultsQuery("k");

        Task<string> Next()
        {
            calls++;
            return Task.FromResult($"result-{calls}");
        }

        (await behavior.HandleAsync(request, Next)).Should().Be("result-1");
        (await behavior.HandleAsync(request, Next)).Should().Be("result-2");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WhenTheScopeIsNotCacheable_NeitherReadsNorWritesTheStore()
    {
        var store = CreateMissingStore();
        var behavior = CreateBehavior<TaggedQuery>(store, new MutableScopeProvider(CacheKeyScope.NotCacheable));
        var request = new TaggedQuery("k", null, ["orders"]);

        var response = await behavior.HandleAsync(request, () => Task.FromResult("result"));

        response.Should().Be("result");
        await store.DidNotReceive().GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        _index.ListKeys("orders").Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenTheScopeBecomesCacheable_CachesFromThenOn()
    {
        var calls = 0;
        var scopeProvider = new MutableScopeProvider(CacheKeyScope.NotCacheable);
        var behavior = CreateBehavior<DefaultsQuery>(scopeProvider: scopeProvider);
        var request = new DefaultsQuery("k");

        Task<string> Next()
        {
            calls++;
            return Task.FromResult($"result-{calls}");
        }

        (await behavior.HandleAsync(request, Next)).Should().Be("result-1");

        scopeProvider.Scope = CacheKeyScope.For("tenant:acme");

        // Nothing was written while uncacheable, so the handler runs once more and that result is
        // the one the resolved scope caches.
        (await behavior.HandleAsync(request, Next)).Should().Be("result-2");
        (await behavior.HandleAsync(request, Next)).Should().Be("result-2");

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_SecondCall_IsServedFromTheCache()
    {
        var calls = 0;
        var behavior = CreateBehavior<DefaultsQuery>();
        var request = new DefaultsQuery("k");

        Task<string> Next()
        {
            calls++;
            return Task.FromResult($"result-{calls}");
        }

        (await behavior.HandleAsync(request, Next)).Should().Be("result-1");
        (await behavior.HandleAsync(request, Next)).Should().Be("result-1");

        calls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_DifferentCacheKeys_AreCachedSeparately()
    {
        var behavior = CreateBehavior<DefaultsQuery>();

        (await behavior.HandleAsync(new DefaultsQuery("a"), () => Task.FromResult("first"))).Should().Be("first");
        (await behavior.HandleAsync(new DefaultsQuery("b"), () => Task.FromResult("second"))).Should().Be("second");

        // The first key must still hold its own value rather than the one cached for "b".
        (await behavior.HandleAsync(new DefaultsQuery("a"), () => Task.FromResult("third"))).Should().Be("first");
    }

    [Fact]
    public async Task HandleAsync_NullResult_IsNotCached()
    {
        var calls = 0;
        var behavior = CreateBehavior<DefaultsQuery>();

        Task<string> Next()
        {
            calls++;
            return Task.FromResult<string>(null!);
        }

        (await behavior.HandleAsync(new DefaultsQuery("k"), Next)).Should().BeNull();
        (await behavior.HandleAsync(new DefaultsQuery("k"), Next)).Should().BeNull();

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_NonPositiveLifetime_SkipsTheWrite()
    {
        var store = CreateMissingStore();
        var request = new TaggedQuery("k", TimeSpan.Zero, ["orders"]);

        var response = await CreateBehavior<TaggedQuery>(store).HandleAsync(request, () => Task.FromResult("result"));

        response.Should().Be("result");
        await store.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        _index.ListKeys("orders").Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CachedResult_IsTaggedForInvalidation()
    {
        var behavior = CreateBehavior<TaggedQuery>();
        var request = new TaggedQuery("k", null, ["orders"]);

        await behavior.HandleAsync(request, () => Task.FromResult("result"));

        _index.ListKeys("orders").Should().ContainSingle()
            .Which.Should().Be(KeyBuilder.Build(request));
    }

    [Fact]
    public async Task HandleAsync_TagAssociation_ExpiresWithTheEntry()
    {
        var behavior = CreateBehavior<TaggedQuery>();
        var request = new TaggedQuery("k", TimeSpan.FromMinutes(2), ["orders"]);

        await behavior.HandleAsync(request, () => Task.FromResult("result"));
        _time.Advance(TimeSpan.FromMinutes(3));

        _index.ListKeys("orders").Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_InvalidatedTag_CausesTheHandlerToRunAgain()
    {
        var calls = 0;
        var store = new MemoryCacheStore(_cache);
        var behavior = CreateBehavior<TaggedQuery>(store);
        var invalidator = new CacheInvalidator(store, _index, Options.Create(_options));
        var request = new TaggedQuery("k", null, ["orders"]);

        Task<string> Next()
        {
            calls++;
            return Task.FromResult($"result-{calls}");
        }

        await behavior.HandleAsync(request, Next);
        await invalidator.InvalidateTagAsync("orders");

        (await behavior.HandleAsync(request, Next)).Should().Be("result-2");
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutLifetime_UsesTheConfiguredDefault()
    {
        var store = CreateMissingStore();
        _options.DefaultTimeToLive = TimeSpan.FromMinutes(7);

        await CreateBehavior<DefaultsQuery>(store).HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"));

        await store.Received(1).SetAsync(
            Arg.Any<string>(),
            "result",
            TimeSpan.FromMinutes(7),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RequestWithOwnLifetime_OverridesTheDefault()
    {
        var store = CreateMissingStore();
        var request = new TaggedQuery("k", TimeSpan.FromSeconds(30), []);

        await CreateBehavior<TaggedQuery>(store).HandleAsync(request, () => Task.FromResult("result"));

        await store.Received(1).SetAsync(
            Arg.Any<string>(),
            "result",
            TimeSpan.FromSeconds(30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassesTheCancellationTokenToTheStore()
    {
        using var cts = new CancellationTokenSource();
        var store = CreateMissingStore();

        await CreateBehavior<DefaultsQuery>(store)
            .HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"), cts.Token);

        await store.Received(1).GetAsync<string>(Arg.Any<string>(), cts.Token);
        await store.Received(1).SetAsync(Arg.Any<string>(), "result", Arg.Any<TimeSpan>(), cts.Token);
    }

    /// <summary>Stands in for a distributed cache whose server is unreachable.</summary>
    private sealed class UnreachableStoreException : Exception
    {
        public UnreachableStoreException() : base("No connection is available to service this operation.")
        {
        }
    }

    [Fact]
    public async Task HandleAsync_LookupFails_ServesTheQueryFromItsHandler()
    {
        // A cache outage must not fail a query whose handler is perfectly healthy.
        var store = Substitute.For<ICacheStore>();
        store.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CacheLookupResult<string>>(_ => throw new UnreachableStoreException());

        var result = await CreateBehavior<DefaultsQuery>(store)
            .HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"));

        result.Should().Be("result");
    }

    [Fact]
    public async Task HandleAsync_WriteFails_StillReturnsTheHandlersResult()
    {
        // The handler has already done its work here, so failing now would throw that work away
        // over a cache that is merely unavailable.
        var store = CreateMissingStore();
        store.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new UnreachableStoreException());

        var result = await CreateBehavior<DefaultsQuery>(store)
            .HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"));

        result.Should().Be("result");
    }

    [Fact]
    public async Task HandleAsync_WriteFails_LeavesTheTagIndexUntouched()
    {
        // Associating a key the store does not hold would make an invalidation report success for
        // an entry that was never written.
        var store = CreateMissingStore();
        store.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new UnreachableStoreException());
        var request = new TaggedQuery("k", TimeSpan.FromMinutes(5), ["people"]);

        await CreateBehavior<TaggedQuery>(store).HandleAsync(request, () => Task.FromResult("result"));

        _index.ListKeys("people").Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TagIndexThrows_StillReturnsTheHandlersResult()
    {
        // ICacheTagIndex is a documented replaceable seam whose implementations may do I/O. A
        // throwing Associate must not turn a successful handler into a 500: the store write already
        // succeeded, and the association is the least important part of the operation.
        var tagIndex = Substitute.For<ICacheTagIndex>();
        tagIndex.When(i => i.Associate(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => throw new UnreachableStoreException());

        var behavior = new CachingBehavior<TaggedQuery, string>(
            new MemoryCacheStore(_cache), KeyBuilder, tagIndex, Options.Create(_options), _time, Logger<TaggedQuery>());

        var result = await behavior.HandleAsync(
            new TaggedQuery("k", TimeSpan.FromMinutes(5), ["people"]),
            () => Task.FromResult("result"));

        result.Should().Be("result");
    }

    [Fact]
    public async Task HandleAsync_TagIndexThrows_StillPropagatesCancellation()
    {
        // Cancellation stays outside the degrade boundary, exactly as it does for the store guards.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var tagIndex = Substitute.For<ICacheTagIndex>();
        tagIndex.When(i => i.Associate(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => throw new OperationCanceledException(cts.Token));

        var behavior = new CachingBehavior<TaggedQuery, string>(
            new MemoryCacheStore(_cache), KeyBuilder, tagIndex, Options.Create(_options), _time, Logger<TaggedQuery>());

        var act = async () => await behavior.HandleAsync(
            new TaggedQuery("k", TimeSpan.FromMinutes(5), ["people"]),
            () => Task.FromResult("result"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HandleAsync_UnserializableResponse_StillReturnsIt()
    {
        // Serialize reports what it cannot write as CacheSerializationException, which means the
        // same thing as an unavailable store: this response is not cacheable.
        var store = CreateMissingStore();
        store.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new CacheSerializationException("unwritable", new NotSupportedException()));

        var result = await CreateBehavior<DefaultsQuery>(store)
            .HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"));

        result.Should().Be("result");
    }

    [Fact]
    public async Task HandleAsync_CallerCancelledDuringLookup_PropagatesTheCancellation()
    {
        // Degrading here would run a handler nobody is waiting for.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var store = Substitute.For<ICacheStore>();
        store.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CacheLookupResult<string>>(_ => throw new OperationCanceledException(cts.Token));

        var act = async () => await CreateBehavior<DefaultsQuery>(store)
            .HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HandleAsync_CallerCancelledDuringWrite_PropagatesTheCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var store = CreateMissingStore();
        store.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException(cts.Token));

        var act = async () => await CreateBehavior<DefaultsQuery>(store)
            .HandleAsync(new DefaultsQuery("k"), () => Task.FromResult("result"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
