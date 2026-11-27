namespace Bsft.Data;

/// <summary>
/// The default size limits of <c>in</c> sets, which a query configuration raises or lowers with
/// <see cref="QueryConfigurationBuilder{TEntity, TDto}.MaxInValues"/> and
/// <see cref="QueryConfigurationBuilder{TEntity, TDto}.MaxInValuesPerFilter"/>.
/// </summary>
public static class FilterInLimits
{
    /// <summary>The number of values one set may hold unless a query configuration says otherwise.</summary>
    public const int DefaultMaxValuesPerSet = 100;

    /// <summary>
    /// The number of values all sets of one filter may hold together unless a query configuration says
    /// otherwise. Well below the 2100 parameters SQL Server accepts per command, which EF Core 10 spends
    /// one per value (and pads), next to the parameters of the filter's other comparisons.
    /// </summary>
    public const int DefaultMaxValuesPerFilter = 500;
}
