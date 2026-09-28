using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Bsft.Caching;

/// <summary>
/// <see cref="ICacheStore"/> over an <see cref="IDistributedCache"/>. Values cross the process
/// boundary as serialized payloads, so a cached result must be serializable and is returned as a
/// fresh instance on every hit.
/// </summary>
public sealed class DistributedCacheStore : ICacheStore
{
    /// <summary>How many bytes of a key's hash identify it in the log.</summary>
    private const int KeyDigestBytes = 8;

    private readonly IDistributedCache _cache;
    private readonly ICacheSerializer _serializer;
    private readonly ILogger<DistributedCacheStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedCacheStore"/> class.
    /// </summary>
    /// <param name="cache">The underlying distributed cache.</param>
    /// <param name="serializer">The serializer converting values to and from the stored payload.</param>
    /// <param name="logger">The logger recording unreadable payloads.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public DistributedCacheStore(
        IDistributedCache cache,
        ICacheSerializer serializer,
        ILogger<DistributedCacheStore> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CacheLookupResult<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        var payload = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return new CacheLookupResult<T>(false, default);

        T? value;
        try
        {
            value = _serializer.Deserialize<T>(payload);
        }
        // An entry written before the response type changed is unreadable but harmless: it is
        // dropped and reported as a miss, so the handler runs instead of the request failing for
        // the whole remaining lifetime of the stale entry.
        catch (CacheSerializationException ex)
        {
            // The key embeds the scope and the caller's discriminator, which may carry personal
            // data, so only a digest of it reaches the log.
            _logger.LogWarning(
                ex,
                "Dropping unreadable cache entry for {ResponseType} (key digest {KeyDigest}).",
                typeof(T).Name,
                Digest(key));

            await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

            return new CacheLookupResult<T>(false, default);
        }

        // A payload that deserializes to null carries no usable value; treating it as a miss keeps
        // the behavior from handing a null result to a caller expecting a cached one.
        return value is null
            ? new CacheLookupResult<T>(false, default)
            : new CacheLookupResult<T>(true, value);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive };

        return _cache.SetAsync(key, _serializer.Serialize(value), entryOptions, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        return _cache.RemoveAsync(key, cancellationToken);
    }

    private static string Digest(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, KeyDigestBytes));
}
