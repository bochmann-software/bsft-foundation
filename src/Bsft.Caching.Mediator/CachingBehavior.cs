using Bsft.Caching.Invalidation;
using Bsft.Caching.Keys;
using Bsft.Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Caching.Mediator;

/// <summary>
/// Mediator pipeline behavior that serves queries marked with <see cref="ICacheable"/> from the
/// configured cache store and writes the handler's result back on a miss. Anything else passes
/// through without entering an async state machine, so the behavior adds no overhead to uncached
/// requests.
/// </summary>
/// <remarks>
/// A cache is an optimisation, so an unavailable one degrades the application rather than failing
/// it: a store that cannot be read is treated as a miss and the handler runs, and a store that
/// cannot be written leaves the handler's result on its way to the caller. The tag index sits inside
/// the same boundary, because it is a replaceable seam whose implementations may do I/O as well.
/// This is the same stance the store already takes towards an unreadable payload. Cancellation is
/// deliberately excluded —
/// a caller who cancelled asked for exactly that, and turning it into a degraded pass-through would
/// run a handler nobody is waiting for.
/// <para>
/// A request whose ambient scope is <see cref="CacheKeyScope.NotCacheable"/> bypasses the cache
/// entirely — no read and no write — because it has no discriminator that would keep its entries
/// apart from the next such request's.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ICacheStore _store;
    private readonly ICacheKeyBuilder _keyBuilder;
    private readonly ICacheTagIndex _tagIndex;
    private readonly BsftCachingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="store">The cache store holding the results.</param>
    /// <param name="keyBuilder">The builder producing the fully qualified cache key.</param>
    /// <param name="tagIndex">The index recording which keys carry which tag.</param>
    /// <param name="options">The caching options supplying the default lifetime.</param>
    /// <param name="timeProvider">The time source used to compute when an entry expires.</param>
    /// <param name="logger">The logger recording a cache store that could not be reached.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CachingBehavior(
        ICacheStore store,
        ICacheKeyBuilder keyBuilder,
        ICacheTagIndex tagIndex,
        IOptions<BsftCachingOptions> options,
        TimeProvider timeProvider,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
        _tagIndex = tagIndex ?? throw new ArgumentNullException(nameof(tagIndex));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        if (next is null)
            throw new ArgumentNullException(nameof(next));

        // The marker is honoured for queries only. A command carrying it — directly, through a
        // shared base type, or by implementing both contracts — must still reach its handler,
        // because serving it from the cache would drop its write along with every check the
        // handler performs.
        return request is IQuery<TResponse> and not ICommand<TResponse> and ICacheable cacheable
            ? HandleCacheableAsync(cacheable, next, cancellationToken)
            : next();
    }

    private async Task<TResponse> HandleCacheableAsync(
        ICacheable cacheable,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var key = _keyBuilder.Build(cacheable);

        // No key means the ambient scope declared this request uncacheable — an unresolved tenant,
        // say. Neither reading nor writing is safe then: without a discriminator every such request
        // would share one bucket, so the handler answers and nothing is stored.
        if (key is null)
            return await next().ConfigureAwait(false);

        var lookup = await LookUpAsync(key, cancellationToken).ConfigureAwait(false);
        if (lookup.IsHit)
            return lookup.Value!;

        var response = await next().ConfigureAwait(false);

        // A null result is not cached: it is indistinguishable from a miss on read, so caching it
        // would only add a write per request without ever producing a hit.
        if (response is null)
            return response;

        var timeToLive = cacheable.TimeToLive ?? _options.DefaultTimeToLive;

        // A non-positive lifetime is the caller opting this one request out of caching, rather
        // than a reason to fail a query whose handler already produced a result.
        if (timeToLive <= TimeSpan.Zero)
            return response;

        await StoreAsync(key, response, timeToLive, cacheable, cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Reads the cached result, reporting a store that could not be reached as a miss so the
    /// handler runs.
    /// </summary>
    /// <remarks>
    /// This and <see cref="StoreAsync"/> share a shape rather than a helper. Folding them into one
    /// generic degrade-on-failure wrapper would put a closure on the path of every cacheable request
    /// to save a catch block, and would replace two explanations of what is being traded away with
    /// one that fits neither: a failed read costs a cache hit, a failed write throws away a place to
    /// put work that is already done.
    /// </remarks>
    private async Task<CacheLookupResult<TResponse>> LookUpAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await _store.GetAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // The store is arbitrary infrastructure — a Redis client raises its own connection type,
        // which this assembly cannot name — so the catch has to be wide. Nothing is hidden: the
        // failure is logged, and the only consequence is that the handler runs.
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Cache lookup for {RequestType} failed; serving the request from its handler.",
                typeof(TRequest).Name);

            return new CacheLookupResult<TResponse>(false, default);
        }
    }

    /// <summary>
    /// Writes the handler's result back, keeping a store that could not be written from failing a
    /// query whose work is already done.
    /// </summary>
    private async Task StoreAsync(
        string key,
        TResponse response,
        TimeSpan timeToLive,
        ICacheable cacheable,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.SetAsync(key, response, timeToLive, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Same boundary as the lookup, and the stakes are higher here: the handler has already
        // produced the answer, so failing now would throw away completed work over a cache that is
        // merely unavailable. A value that cannot be serialized arrives here too, as
        // CacheSerializationException, and means the same thing — this response is not cacheable.
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Caching the result of {RequestType} failed; returning it uncached.",
                typeof(TRequest).Name);

            return;
        }

        // Associate only after a successful write, so the index never points at a key the store
        // does not hold.
        Associate(key, cacheable, timeToLive, cancellationToken);
    }

    /// <summary>
    /// Records the entry's tags, keeping a tag index that could not be reached from failing a query
    /// whose result is already written.
    /// </summary>
    /// <remarks>
    /// The in-process index cannot fail, but <see cref="ICacheTagIndex"/> is a replaceable seam and a
    /// shared implementation does I/O, so it belongs inside the same degrade boundary as the store.
    /// <para>
    /// A failed association leaves the entry in the store carrying no tag, which is exactly the state
    /// an untagged cacheable query produces: it can still be invalidated by key and still expires on
    /// its own, but a later <c>InvalidateTagAsync</c> will not reach it, so it may serve a stale
    /// result for the rest of its lifetime. The stamp protocol is unaffected — a write that recorded
    /// nothing hands out no stamp, so the conditional forget in <c>CacheInvalidator</c> keeps
    /// comparing against associations that really exist. Removing the entry again to keep store and
    /// index consistent would be the opposite trade: it turns an unreachable index into a lost cache
    /// write plus a second round trip that can fail in turn.
    /// </para>
    /// </remarks>
    private void Associate(string key, ICacheable cacheable, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        try
        {
            _tagIndex.Associate(key, cacheable.CacheTags, _timeProvider.GetUtcNow() + timeToLive);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Same boundary and the same reasoning as the store guards: the index is arbitrary
        // infrastructure this assembly cannot name the failure types of.
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Tagging the cached result of {RequestType} failed; it stays cached but untagged.",
                typeof(TRequest).Name);
        }
    }
}
