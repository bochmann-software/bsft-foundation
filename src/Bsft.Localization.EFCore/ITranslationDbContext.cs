using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.EFCore;

/// <summary>
/// Defines the database context contract for accessing translation entities.
/// </summary>
public interface ITranslationDbContext
{
    /// <summary>
    /// Gets the set of translation entities.
    /// </summary>
    DbSet<TranslationEntity> Translations { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
