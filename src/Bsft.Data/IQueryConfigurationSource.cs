namespace Bsft.Data;

/// <summary>
/// Exposes the parts of a compiled query configuration that startup validation checks, so a
/// validator can read them without reflecting over generic types.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="Search.ISearchGroupSource"/> for the non-search half of a
/// configuration. Both exist for the same reason: a configuration is registered under a closed
/// generic type per entity and DTO pair, which no validator can enumerate.
/// </remarks>
public interface IQueryConfigurationSource
{
    /// <summary>
    /// Gets the name of the <see cref="QueryConfigurationBase{TEntity, TDto}"/> implementation, so an
    /// error can name the file to open rather than the compiled wrapper around it.
    /// </summary>
    string ConfigurationName { get; }

    /// <summary>Gets the field the configuration sorts by when a request states no order, if any.</summary>
    string? DefaultSortField { get; }

    /// <summary>Checks whether a field is declared and may be sorted on.</summary>
    /// <param name="fieldName">The DTO field name to check.</param>
    /// <returns>True if the field is mapped and sortable; otherwise false.</returns>
    bool IsSortable(string fieldName);
}
