using Bsft.Sample.Inventory.Core;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Sample.Orders.Core;

/// <summary>
/// Database contract for the Orders module. Exposes only the tables
/// that the module needs, keeping the dependency surface explicit.
/// </summary>
public interface IOrderDbContext
{
    /// <summary>
    /// Gets the set of orders.
    /// </summary>
    DbSet<Order> Orders { get; }

    /// <summary>
    /// Gets the set of products (read-only cross-module reference for
    /// resolving product name and price when creating an order).
    /// </summary>
    DbSet<Product> Products { get; }

    /// <summary>
    /// Persists all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the save operation.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
