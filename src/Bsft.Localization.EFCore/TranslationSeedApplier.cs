using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.EFCore;

/// <summary>
/// Applies translation seed data to the database at startup,
/// using an insert-if-not-exists strategy to preserve existing entries.
/// </summary>
public static class TranslationSeedApplier
{
    /// <summary>
    /// Inserts seed translations that do not yet exist in the database.
    /// </summary>
    /// <param name="dbContext">The translation database context.</param>
    /// <param name="seed">The seed data to apply.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// <para>
    /// What is already stored is read once, not asked about once per entry: this runs on every start
    /// with every module's seed in it, and a framework with a few thousand strings would otherwise
    /// open a start-up with a few thousand round trips.
    /// </para>
    /// <para>
    /// A stored row counts as the seed's row whatever the spelling of its locale or key, the way the
    /// seed itself and the resolution chain treat the two. Under a case-insensitive collation the
    /// alternative is not an extra row but a start that fails on the unique index.
    /// </para>
    /// <para>
    /// Read and write are not one transaction, and two instances starting together therefore race:
    /// both read a store without the row, both insert it, and the unique index refuses the second.
    /// Seeding is idempotent by intent, so a refused write is answered by reading what the store holds
    /// now and inserting what is still missing — once. A second refusal is a real one and is thrown.
    /// </para>
    /// </remarks>
    public static async Task ApplyAsync(
        this ITranslationDbContext dbContext,
        TranslationSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(seed);

        var entries = seed.GetEntries();

        if (await TryInsertMissingAsync(dbContext, entries, retryable: true, cancellationToken).ConfigureAwait(false))
            return;

        await TryInsertMissingAsync(dbContext, entries, retryable: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts the seed entries the store does not hold yet.
    /// </summary>
    /// <param name="dbContext">The translation database context.</param>
    /// <param name="entries">The seed, by locale and key.</param>
    /// <param name="retryable">Whether a refused write is answered with <see langword="false"/> rather than thrown.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns><see langword="true"/> when the store holds the seed afterwards.</returns>
    /// <exception cref="DbUpdateException">
    /// Thrown when the write is refused and <paramref name="retryable"/> is <see langword="false"/>.
    /// </exception>
    private static async Task<bool> TryInsertMissingAsync(
        ITranslationDbContext dbContext,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> entries,
        bool retryable,
        CancellationToken cancellationToken)
    {
        var stored = await StoredGlobalKeysAsync(dbContext, cancellationToken).ConfigureAwait(false);
        var inserted = new List<TranslationEntity>();

        foreach (var (locale, translations) in entries)
        {
            foreach (var (key, value) in translations)
            {
                if (!IsNew(stored, locale, key))
                    continue;

                var row = new TranslationEntity
                {
                    Key = key,
                    Locale = locale,
                    Value = value,
                    TenantId = null
                };

                dbContext.Translations.Add(row);
                inserted.Add(row);
            }
        }

        if (inserted.Count == 0)
            return true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException) when (retryable)
        {
            // The whole batch rolled back, so the rows that did not clash are still missing. Letting
            // go of what this pass added leaves the context able to make the second one.
            foreach (var row in inserted)
                dbContext.Translations.Remove(row);

            return false;
        }
    }

    /// <summary>Reads which global rows the store already holds, by locale and key.</summary>
    /// <param name="dbContext">The translation database context.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The keys the store holds, per locale.</returns>
    private static async Task<Dictionary<string, HashSet<string>>> StoredGlobalKeysAsync(
        ITranslationDbContext dbContext, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Translations
            .AsNoTracking()
            .Where(t => t.TenantId == null)
            .Select(t => new { t.Locale, t.Key })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var stored = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
            KeysOf(stored, row.Locale).Add(row.Key);

        return stored;
    }

    /// <summary>
    /// Reports whether a seed entry still has to be inserted, and records it as inserted if so.
    /// </summary>
    /// <param name="stored">What the store holds, extended with what this pass decides to insert.</param>
    /// <param name="locale">The locale of the seed entry.</param>
    /// <param name="key">The key of the seed entry.</param>
    /// <returns><see langword="true"/> when the entry is not in the store yet.</returns>
    private static bool IsNew(Dictionary<string, HashSet<string>> stored, string locale, string key)
        => KeysOf(stored, locale).Add(key);

    /// <summary>Reads the keys held for one locale, opening the set when it is the first.</summary>
    /// <param name="stored">What the store holds.</param>
    /// <param name="locale">The locale to read.</param>
    /// <returns>The set of keys of that locale.</returns>
    private static HashSet<string> KeysOf(Dictionary<string, HashSet<string>> stored, string locale)
    {
        if (stored.TryGetValue(locale, out var keys))
            return keys;

        keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        stored[locale] = keys;

        return keys;
    }
}
