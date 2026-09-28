using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.EFCore;

/// <summary>
/// Reads translations for the resolution chain out of the application's own database.
/// </summary>
/// <remarks>
/// <para>
/// Every read materialises the rows before projecting them, so the query the provider issues is the
/// plain "select the entity" the mapping describes and the projection is not part of it. The chain
/// then resolves in memory, which is what keeps a bundle to one read rather than one per key.
/// </para>
/// <para>
/// Nothing read here is ever written back, so no read is tracked: a bundle is every row of a locale,
/// and change-tracking all of them would cost a snapshot per row for a write that never comes.
/// </para>
/// </remarks>
internal sealed class EFCoreTranslationReader : ITranslationReader
{
    private readonly ITranslationDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreTranslationReader"/> class.
    /// </summary>
    /// <param name="dbContext">The application's context holding the translation table.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext"/> is null.</exception>
    public EFCoreTranslationReader(ITranslationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForLocale(string locale, string? tenantId)
        => _dbContext.Translations
            .AsNoTracking()
            .Where(t => t.Locale == locale && (t.TenantId == tenantId || t.TenantId == null))
            .ToList()
            .ConvertAll(Project);

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForKey(string key, string locale, string fallbackLocale, string? tenantId)
        => _dbContext.Translations
            .AsNoTracking()
            .Where(t => t.Key == key
                && (t.Locale == locale || t.Locale == fallbackLocale)
                && (t.TenantId == tenantId || t.TenantId == null))
            .ToList()
            .ConvertAll(Project);

    /// <inheritdoc />
    public IReadOnlyList<Translation> ListForLocaleOrFallback(string locale, string fallbackLocale, string? tenantId)
        => _dbContext.Translations
            .AsNoTracking()
            .Where(t => t.Locale == locale || t.Locale == fallbackLocale)
            .Where(t => t.TenantId == tenantId || t.TenantId == null)
            .ToList()
            .ConvertAll(Project);

    /// <inheritdoc />
    /// <remarks>
    /// The only read without <c>AsNoTracking</c>, and it needs none: a column is not an entity, so
    /// there is nothing here for the change tracker to hold on to in the first place.
    /// </remarks>
    public IReadOnlyList<string> ListLocales()
        => _dbContext.Translations
            .Select(t => t.Locale)
            .Distinct()
            .ToList();

    /// <summary>Reduces a stored row to the shape the resolution chain reads.</summary>
    /// <param name="entity">The stored row.</param>
    /// <returns>The row without its surrogate key, which nothing outside this package needs.</returns>
    private static Translation Project(TranslationEntity entity)
        => new(entity.Key, entity.Locale, entity.Value, entity.TenantId);
}
