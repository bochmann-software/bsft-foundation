namespace Bsft.Localization;

/// <summary>What one resolved translation is cached under.</summary>
/// <remarks>
/// <para>
/// A key of its own rather than the three parts joined into a string. A joined key needs a stand-in
/// for the absent tenant and a separator that none of the parts may contain, and neither holds:
/// a tenant whose id is literally the stand-in, or a key containing the separator, would read
/// another scope's entry. The parts are compared as parts here, so no spelling can collide.
/// </para>
/// <para>
/// All three parts are compared ordinally and case sensitively. The store may be case sensitive
/// (PostgreSQL, SQLite), so two spellings of a key can be two rows with two values, and a cache that
/// merged them would make the answer depend on which spelling was asked first. An absent tenant
/// equals only an absent tenant. The hash is taken once, when the key is built, so a cache that must
/// not hash under a lock does not.
/// </para>
/// </remarks>
internal readonly struct TranslationCacheKey : IEquatable<TranslationCacheKey>
{
    private readonly int _hash;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationCacheKey"/> struct.
    /// </summary>
    /// <param name="tenantId">The tenant it was resolved for, or <see langword="null"/> for none.</param>
    /// <param name="locale">The locale it was resolved for.</param>
    /// <param name="key">The translation key.</param>
    public TranslationCacheKey(string? tenantId, string locale, string key)
    {
        TenantId = tenantId;
        Locale = locale;
        Key = key;

        var hash = new HashCode();
        hash.Add(tenantId is null ? -1 : string.GetHashCode(tenantId, StringComparison.Ordinal));
        hash.Add(string.GetHashCode(locale, StringComparison.Ordinal));
        hash.Add(string.GetHashCode(key, StringComparison.Ordinal));
        _hash = hash.ToHashCode();
    }

    /// <summary>Gets the tenant it was resolved for, or <see langword="null"/> for none.</summary>
    public string? TenantId { get; }

    /// <summary>Gets the locale it was resolved for.</summary>
    public string Locale { get; }

    /// <summary>Gets the translation key.</summary>
    public string Key { get; }

    /// <inheritdoc />
    public bool Equals(TranslationCacheKey other)
        => string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)
           && string.Equals(Locale, other.Locale, StringComparison.Ordinal)
           && string.Equals(Key, other.Key, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TranslationCacheKey other && Equals(other);

    /// <summary>Whether two keys name the same cache entry.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when both name the same entry.</returns>
    public static bool operator ==(TranslationCacheKey left, TranslationCacheKey right) => left.Equals(right);

    /// <summary>Whether two keys name different cache entries.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when they name different entries.</returns>
    public static bool operator !=(TranslationCacheKey left, TranslationCacheKey right) => !left.Equals(right);

    /// <inheritdoc />
    public override int GetHashCode() => _hash;
}
