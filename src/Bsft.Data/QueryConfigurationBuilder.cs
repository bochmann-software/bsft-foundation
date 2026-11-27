using System.Linq.Expressions;
using System.Reflection;
using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data;

/// <summary>
/// Fluent API for building a query configuration that maps DTO fields to entity properties.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDto">The DTO type.</typeparam>
public sealed class QueryConfigurationBuilder<TEntity, TDto>
{
    /// <summary>The page size ceiling applied until a configuration raises or lowers it.</summary>
    private const int DefaultMaxPageSize = 100;

    private readonly List<FieldBuilder<TEntity, TDto>> _fields = [];
    private readonly List<SearchGroupBuilder<TEntity>> _searchGroups = [];
    private readonly HashSet<string> _searchGroupNames = new(StringComparer.Ordinal);
    private readonly List<(string Prefix, IDynamicFieldResolver<TEntity> Resolver)> _dynamicFields = [];
    private int _maxPageSize = DefaultMaxPageSize;
    private string? _defaultSortField;
    private SortDirection _defaultSortDirection = SortDirection.Asc;
    private FilterStringMatch _stringMatch = FilterStringMatch.LowerInvariant;
    private int _maxInValuesPerSet = FilterInLimits.DefaultMaxValuesPerSet;
    private int _maxInValuesPerFilter = FilterInLimits.DefaultMaxValuesPerFilter;

    /// <summary>
    /// Begins configuring a DTO field mapping.
    /// </summary>
    /// <param name="dtoProperty">An expression selecting the DTO property.</param>
    /// <returns>A field builder for further configuration.</returns>
    public FieldBuilder<TEntity, TDto> Field(Expression<Func<TDto, object?>> dtoProperty)
    {
        if (dtoProperty is null)
            throw new ArgumentNullException(nameof(dtoProperty));

        var memberName = ExtractMemberName(dtoProperty);
        var defaultEntityExpression = BuildDefaultEntityExpression(memberName);
        var builder = new FieldBuilder<TEntity, TDto>(memberName, defaultEntityExpression);
        _fields.Add(builder);
        return builder;
    }

    /// <summary>
    /// Declares a named group of index-backed searchable columns. Declaring it here keeps a module's
    /// whole query surface — filters, sorting and search — in one place.
    /// </summary>
    /// <param name="name">The group name a search request refers to.</param>
    /// <param name="configure">A callback declaring the fields, mode and language of the group.</param>
    /// <returns>The group builder the callback configured.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public SearchGroupBuilder<TEntity> Searchable(string name, Action<SearchGroupBuilder<TEntity>> configure)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A search group name is required.", nameof(name));
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        // A duplicate name would otherwise surface as a dictionary collision inside the executor on
        // the first real search, long after the mistake was made.
        if (_searchGroupNames.Contains(name))
            throw new ArgumentException($"Search group '{name}' is already declared.", nameof(name));

        var builder = new SearchGroupBuilder<TEntity>(name);
        configure(builder);
        _searchGroupNames.Add(name);
        _searchGroups.Add(builder);
        return builder;
    }

    /// <summary>
    /// Declares a family of dynamic fields: every filter or sort field whose name starts with
    /// <paramref name="prefix"/> is resolved by <paramref name="resolver"/> at request time instead of
    /// by a DTO property, so fields that exist per tenant (configured extension fields, for instance)
    /// take part in the same whitelist as the declared ones.
    /// </summary>
    /// <param name="prefix">
    /// The prefix, such as <c>custom.</c>, matched case-insensitively like every field name. The rest of
    /// the name is the key the resolver is asked about.
    /// </param>
    /// <param name="resolver">
    /// The resolver, asked on every request; see <see cref="IDynamicFieldResolver{TEntity}"/> for its
    /// contract and <see cref="DynamicField{TEntity}"/> for what a missing value means.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="prefix"/> is null or whitespace, or overlaps a prefix declared before.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver"/> is null.</exception>
    /// <remarks>
    /// A declared field whose name starts with the prefix is refused when the configuration is built,
    /// since which of the two a request reached would then depend on the order of a lookup. A name
    /// that is only the prefix, or that the resolver does not know, is an unknown field: a <c>400</c>,
    /// exactly as for an undeclared DTO field.
    /// </remarks>
    public void DynamicFields(string prefix, IDynamicFieldResolver<TEntity> resolver)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("A dynamic field prefix is required.", nameof(prefix));
        if (resolver is null)
            throw new ArgumentNullException(nameof(resolver));

        foreach (var (declared, _) in _dynamicFields)
        {
            if (declared.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith(declared, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Dynamic field prefix '{prefix}' overlaps the prefix '{declared}' declared before.", nameof(prefix));
            }
        }

        _dynamicFields.Add((prefix, resolver));
    }

    /// <summary>
    /// Builds the declared search groups.
    /// </summary>
    internal IReadOnlyList<SearchGroupConfiguration> BuildSearchGroups() => _searchGroups.ConvertAll(b => b.Build());

    /// <summary>
    /// Sets the maximum allowed page size.
    /// </summary>
    /// <param name="max">The maximum page size.</param>
    public void MaxPageSize(int max)
    {
        if (max < 1)
            throw new ArgumentOutOfRangeException(nameof(max), "Max page size must be at least 1.");

        _maxPageSize = max;
    }

    /// <summary>
    /// Sets the most values one <c>in</c> set may hold, <see cref="FilterInLimits.DefaultMaxValuesPerSet"/>
    /// (100) unless set. A larger set is refused with a <c>400</c> carrying
    /// <c>FILTER_IN_TOO_MANY</c> and the limit.
    /// </summary>
    /// <param name="max">The limit, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="max"/> is less than 1.</exception>
    public void MaxInValues(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _maxInValuesPerSet = max;
    }

    /// <summary>
    /// Sets the most values all <c>in</c> sets of one filter may hold together,
    /// <see cref="FilterInLimits.DefaultMaxValuesPerFilter"/> (500) unless set, so that many sets
    /// cannot together send what one set may not. Exceeding it is refused like an oversized set.
    /// </summary>
    /// <param name="max">The limit, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="max"/> is less than 1.</exception>
    /// <remarks>
    /// Raising it is bounded by the provider: SQL Server accepts 2100 parameters per command, and EF Core
    /// 10 sends one (padded) parameter per value there, next to the parameters of every other comparison.
    /// </remarks>
    public void MaxInValuesPerFilter(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _maxInValuesPerFilter = max;
    }

    /// <summary>
    /// Sets the default sort field and direction.
    /// </summary>
    /// <param name="field">An expression selecting the DTO property to sort by default.</param>
    /// <param name="direction">The default sort direction.</param>
    public void DefaultSort(Expression<Func<TDto, object?>> field, SortDirection direction)
    {
        if (field is null)
            throw new ArgumentNullException(nameof(field));

        _defaultSortField = ExtractMemberName(field);
        _defaultSortDirection = direction;
    }

    /// <summary>
    /// Selects how string filters are made case-insensitive for this configuration.
    /// </summary>
    /// <remarks>
    /// The default, <see cref="FilterStringMatch.LowerInvariant"/>, is portable but wraps the column in a
    /// non-sargable <c>LOWER(COALESCE(col, ''))</c>. Opt into <see cref="FilterStringMatch.Collation"/>
    /// only when the filtered columns are declared with a case-insensitive collation, which lets the
    /// query stay index-usable. See <see cref="FilterStringMatch"/> for the full trade-off.
    /// </remarks>
    /// <param name="mode">The case-insensitivity strategy to apply.</param>
    public void StringMatch(FilterStringMatch mode) => _stringMatch = mode;

    /// <summary>
    /// Builds the query field map from the configured fields.
    /// </summary>
    /// <returns>A query field map with all configured mappings.</returns>
    internal QueryFieldMap<TEntity> BuildFieldMap()
    {
        var fieldMap = new QueryFieldMap<TEntity>();
        foreach (var field in _fields)
            field.Apply(fieldMap);

        foreach (var (prefix, resolver) in _dynamicFields)
            fieldMap.AddDynamic(prefix, resolver);

        fieldMap.SetInLimits(_maxInValuesPerSet, _maxInValuesPerFilter);
        return fieldMap;
    }

    /// <summary>
    /// Gets the configured maximum page size.
    /// </summary>
    internal int GetMaxPageSize() => _maxPageSize;

    /// <summary>
    /// Gets the configured default sort field, if any.
    /// </summary>
    internal string? GetDefaultSortField() => _defaultSortField;

    /// <summary>
    /// Gets the configured default sort direction.
    /// </summary>
    internal SortDirection GetDefaultSortDirection() => _defaultSortDirection;

    /// <summary>
    /// Gets the configured case-insensitivity strategy for string filters.
    /// </summary>
    internal FilterStringMatch GetStringMatch() => _stringMatch;

    /// <summary>
    /// Returns this builder instance. Satisfies the builder contract.
    /// </summary>
    /// <returns>This builder instance.</returns>
    public QueryConfigurationBuilder<TEntity, TDto> Build() => this;

    private static string ExtractMemberName(Expression<Func<TDto, object?>> expression)
    {
        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        if (body is MemberExpression member)
            return member.Member.Name;

        throw new ArgumentException("Expression must be a simple property access.", nameof(expression));
    }

    private static LambdaExpression? BuildDefaultEntityExpression(string memberName)
    {
        var entityProperty = typeof(TEntity).GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (entityProperty is null)
            return null;

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var access = Expression.Property(parameter, entityProperty);
        var converted = Expression.Convert(access, typeof(object));
        return Expression.Lambda(converted, parameter);
    }
}
