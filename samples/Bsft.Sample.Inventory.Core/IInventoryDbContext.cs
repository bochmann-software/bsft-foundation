using Microsoft.EntityFrameworkCore;

namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Database contract for the Inventory module. Exposes only the tables
/// that the module needs, keeping the dependency surface explicit.
/// </summary>
public interface IInventoryDbContext
{
    /// <summary>
    /// Gets the set of products.
    /// </summary>
    DbSet<Product> Products { get; }

    /// <summary>
    /// Persists all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the save operation.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
