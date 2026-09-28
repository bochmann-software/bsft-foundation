namespace Bsft.Data.Contracts.Pagination;

/// <summary>
/// Represents a single sort criterion with field name and direction.
/// </summary>
/// <param name="Field">The name of the field to sort by.</param>
/// <param name="Direction">The sort direction. Defaults to ascending.</param>
public record SortItem(string Field, SortDirection Direction = SortDirection.Asc);
