using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.Search;

/// <summary>
/// A query configuration compiled once: the field map, the paging and sorting defaults, and the
/// declared search groups.
/// </summary>
/// <remarks>
/// It is separate from <see cref="SearchExecutor{TEntity, TDto}"/> so that startup validation can
/// read the declared groups without a search provider being registered. That is exactly the
/// situation the validator has to report on — a group declared with no provider behind it — and it
/// must produce a readable error rather than a dependency-injection failure.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDto">The DTO type the configuration is written against.</typeparam>
public sealed class CompiledSearchConfiguration<TEntity, TDto> : ISearchGroupSource, IQueryConfigurationSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompiledSearchConfiguration{TEntity, TDto}"/> class.
    /// </summary>
    /// <param name="configuration">The query configuration to compile.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    public CompiledSearchConfiguration(QueryConfigurationBase<TEntity, TDto> configuration)
    {
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        var builder = new QueryConfigurationBuilder<TEntity, TDto>();
        configuration.Configure(builder);

        ConfigurationName = configuration.GetType().Name;
        FieldMap = builder.BuildFieldMap();
        MaxPageSize = builder.GetMaxPageSize();
        DefaultSortField = builder.GetDefaultSortField();
        DefaultSortDirection = builder.GetDefaultSortDirection();
        Groups = builder.BuildSearchGroups();
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchGroupConfiguration> Groups { get; }

    /// <inheritdoc />
    public string ConfigurationName { get; }

    /// <summary>Gets the compiled field map used for filtering and sorting.</summary>
    public QueryFieldMap<TEntity> FieldMap { get; }

    /// <summary>Gets the maximum page size allowed by the configuration.</summary>
    public int MaxPageSize { get; }

    /// <summary>Gets the default sort field, used as the deterministic tie-breaker for ranked results.</summary>
    public string? DefaultSortField { get; }

    /// <summary>Gets the direction applied to <see cref="DefaultSortField"/> when it is the fallback order.</summary>
    public SortDirection DefaultSortDirection { get; }

    /// <inheritdoc />
    public bool IsSortable(string fieldName) => FieldMap.IsSortable(fieldName);
}
