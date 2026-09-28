using System.Text.Json.Serialization;
using Bsft.Data.Contracts.Pagination;

namespace Bsft.Sample.Inventory.Core;

/// <summary>
/// Read-only DTO returned by product queries. Mapped from Product via convention mapping.
/// </summary>
/// <param name="Id">The unique identifier of the product.</param>
/// <param name="Name">The name of the product.</param>
/// <param name="StockQuantity">The current stock quantity available.</param>
/// <param name="Price">The unit price of the product.</param>
/// <param name="Version">
/// The optimistic-concurrency token of the product as it was read. It is the value the endpoint
/// turns into the strong <c>ETag</c> on a read and requires back as <c>If-Match</c> on a write, so a
/// client holds the version it is editing rather than guessing it.
/// </param>
/// <param name="ETag">
/// The strong entity tag derived from <paramref name="Version"/>, carried on every product a query
/// answers with — a list row as much as a single read. It is what a client echoes as <c>If-Match</c>,
/// which is why a list row carries it: without it a screen would read each product again before
/// editing it, only to be told a version it already had.
/// </param>
public record ProductDto(
    Guid Id,
    string Name,
    int StockQuantity,
    decimal Price,
    Guid Version,
    [property: JsonPropertyName("etag")] string ETag) : IEntityTaggedItem;
