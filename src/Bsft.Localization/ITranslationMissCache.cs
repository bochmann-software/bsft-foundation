namespace Bsft.Localization;

/// <summary>
/// Remembers keys that no level of the resolution chain has, for a short time and in bounded number.
/// </summary>
internal interface ITranslationMissCache
{
    /// <summary>
    /// Tells whether a live miss is remembered for the key.
    /// </summary>
    /// <param name="key">What the miss was recorded under.</param>
    /// <returns><see langword="true"/> when the key was missed and the entry has not expired.</returns>
    bool Contains(TranslationCacheKey key);

    /// <summary>
    /// Remembers a miss, unless the cache is full of live ones.
    /// </summary>
    /// <param name="key">What the miss is recorded under.</param>
    /// <returns><see langword="true"/> when the miss is remembered; <see langword="false"/> when the cache is full of live misses.</returns>
    bool TryAdd(TranslationCacheKey key);

    /// <summary>
    /// Forgets every remembered miss.
    /// </summary>
    void Clear();
}
