using System.ComponentModel.DataAnnotations;
using Bsft.Data.MultiTenancy;

namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Tenant-scoped product entity representing an item in the inventory.
/// </summary>
public class Product : ITenantEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the product.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the product.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current stock quantity available.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price of the product.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Gets or sets the optimistic-concurrency token. It is a fresh value on every write, and
    /// <see cref="ConcurrencyCheckAttribute"/> puts it into the <c>WHERE</c> clause of every update
    /// and delete, so a write against a version another writer has already replaced changes no rows
    /// and surfaces as a concurrency conflict.
    /// </summary>
    /// <remarks>
    /// A <see cref="Guid"/> the handler rolls itself rather than a provider <c>rowversion</c>: the
    /// samples run on SQLite, which has no automatically maintained row-version column, so a token
    /// the application owns is the provider-independent form of the same idea.
    /// </remarks>
    [ConcurrencyCheck]
    public Guid Version { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier that owns this product.
    /// </summary>
    public string? TenantId { get; set; }
}
