using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;

namespace Bsft.Data;

/// <summary>
/// Holds resolved field mappings from DTO field names to entity expressions,
/// including allowed operators and sortability. Uses a whitelist approach
/// where unknown fields are rejected.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class QueryFieldMap<T>
{
    private readonly Dictionary<string, FieldMapping> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _declarationOrder = [];
    private readonly List<(string Prefix, IDynamicFieldResolver<T> Resolver)> _dynamicFields = [];

    /// <summary>
    /// Gets the most values one <c>in</c> set may hold, <see cref="FilterInLimits.DefaultMaxValuesPerSet"/>
    /// unless the query configuration sets it.
    /// </summary>
    internal int MaxInValuesPerSet { get; private set; } = FilterInLimits.DefaultMaxValuesPerSet;

    /// <summary>
    /// Gets the most values all <c>in</c> sets of one filter may hold together,
    /// <see cref="FilterInLimits.DefaultMaxValuesPerFilter"/> unless the query configuration sets it.
    /// </summary>
    internal int MaxInValuesPerFilter { get; private set; } = FilterInLimits.DefaultMaxValuesPerFilter;

    /// <summary>
    /// Sets the size limits of <c>in</c> sets.
    /// </summary>
    /// <param name="perSet">The most values one set may hold.</param>
    /// <param name="perFilter">The most values all sets of one filter may hold together.</param>
    internal void SetInLimits(int perSet, int perFilter)
    {
        MaxInValuesPerSet = perSet;
        MaxInValuesPerFilter = perFilter;
    }

    /// <summary>
    /// Adds a field mapping to the map.
    /// </summary>
    /// <param name="dtoFieldName">The DTO field name.</param>
    /// <param name="entityExpression">The lambda expression pointing to the entity property.</param>
    /// <param name="allowedOperators">The set of allowed filter operators for this field.</param>
    /// <param name="sortable">Whether sorting is allowed on this field.</param>
    internal void Add(string dtoFieldName, LambdaExpression entityExpression, HashSet<FilterOperator> allowedOperators, bool sortable)
    {
        // The declaration order is kept so that a fallback ordering is the same on every run: a
        // dictionary's enumeration order is an implementation detail, and paging by it would be the
        // very non-determinism the fallback exists to remove.
        if (!_fields.ContainsKey(dtoFieldName))
            _declarationOrder.Add(dtoFieldName);

        _fields[dtoFieldName] = new FieldMapping(entityExpression, allowedOperators, sortable);
    }

    /// <summary>
    /// Adds a dynamic field prefix: every field name that starts with it, and is not a declared field,
    /// is resolved by <paramref name="resolver"/> at request time.
    /// </summary>
    /// <param name="prefix">The prefix, matched case-insensitively like every field name.</param>
    /// <param name="resolver">The resolver of the fields under the prefix.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a declared field starts with the prefix: it would hide a dynamic field of the same
    /// name, or be hidden by it, depending on nothing a reader of the configuration could see.
    /// </exception>
    internal void AddDynamic(string prefix, IDynamicFieldResolver<T> resolver)
    {
        foreach (var fieldName in _declarationOrder)
        {
            if (fieldName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Field '{fieldName}' starts with the dynamic field prefix '{prefix}'. Rename the field or choose another prefix.");
        }

        _dynamicFields.Add((prefix, resolver));
    }

    /// <summary>
    /// Resolves a field name that falls under a dynamic prefix.
    /// </summary>
    /// <param name="fieldName">The requested field name.</param>
    /// <param name="field">
    /// The resolved field, or <see langword="null"/> when the name falls under a prefix but the
    /// resolver does not know it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the name falls under a dynamic prefix and is not a declared field;
    /// <see langword="false"/> when it is a declared field or falls under no prefix.
    /// </returns>
    internal bool TryResolveDynamic(string fieldName, out DynamicField<T>? field)
    {
        field = null;
        if (_fields.ContainsKey(fieldName))
            return false;

        foreach (var (prefix, resolver) in _dynamicFields)
        {
            if (!fieldName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var key = fieldName[prefix.Length..];
            field = key.Length == 0 ? null : resolver.Resolve(key);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the entity expression of the first declared sortable field, the last-resort ordering for
    /// a configuration that declares no default sort and whose entity follows no key convention.
    /// </summary>
    /// <returns>The first sortable field's expression, or <see langword="null"/> when none is sortable.</returns>
    internal LambdaExpression? GetFirstSortField()
    {
        foreach (var fieldName in _declarationOrder)
        {
            if (_fields[fieldName] is { IsSortable: true } mapping)
                return mapping.EntityExpression;
        }

        return null;
    }

    /// <summary>
    /// Gets the field mapping for a DTO field name and operator.
    /// </summary>
    /// <param name="fieldName">The DTO field name.</param>
    /// <param name="op">The filter operator being applied.</param>
    /// <returns>The resolved field mapping.</returns>
    /// <exception cref="ValidationException">Thrown when the field is unknown or the operator is not allowed.</exception>
    /// <remarks>
    /// The field name and operator come straight off a client-supplied filter, so a miss is a bad
    /// request, not an internal fault: this throws the framework's <see cref="ValidationException"/> —
    /// which the exception middleware maps to a <c>400</c> keyed on <c>filter</c> — rather than an
    /// <see cref="InvalidOperationException"/> that would surface as a <c>500</c> and leak nothing a
    /// client could act on. The whitelist is the injection boundary: only a declared field, under a
    /// declared operator, is ever resolved to an entity expression.
    /// </remarks>
    public FieldMapping GetField(string fieldName, FilterOperator op)
    {
        if (!_fields.TryGetValue(fieldName, out var mapping))
            throw Invalid("filter", $"Unknown filter field: '{fieldName}'.");

        // An empty operator list allows every operator, as it always has, except 'in': it arrived
        // after fields that name no operators were written, so it is allowed only where a field names it.
        var allowed = mapping.AllowedOperators.Count == 0
            ? op != FilterOperator.In
            : mapping.AllowedOperators.Contains(op);
        if (!allowed)
            throw Invalid("filter", $"Operator '{op}' is not allowed on field '{fieldName}'.");

        return mapping;
    }

    /// <summary>
    /// Gets the entity expression for a sortable DTO field name.
    /// </summary>
    /// <param name="fieldName">The DTO field name.</param>
    /// <returns>The entity expression for sorting.</returns>
    /// <exception cref="ValidationException">Thrown when the field is unknown or not sortable.</exception>
    /// <remarks>
    /// Like <see cref="GetField"/>, the sort field is client-supplied, so an unknown or non-sortable
    /// field is a <c>400</c> keyed on <c>sort</c>, not a <c>500</c>. Startup validation of a
    /// configuration's own default sort field goes through the non-throwing <see cref="IsSortable"/>
    /// instead, so a misconfiguration is reported at startup rather than as a request-time exception.
    /// A name under a dynamic prefix is resolved at request time and sorts by the resolver's sort key;
    /// a field the resolver does not know is as unknown as an undeclared one.
    /// </remarks>
    public LambdaExpression GetSortField(string fieldName)
    {
        if (TryResolveDynamic(fieldName, out var dynamicField))
        {
            if (dynamicField is null)
                throw Invalid("sort", $"Unknown sort field: '{fieldName}'.");

            return dynamicField.Sort ?? throw Invalid("sort", $"Sorting is not allowed on field '{fieldName}'.");
        }

        if (!_fields.TryGetValue(fieldName, out var mapping))
            throw Invalid("sort", $"Unknown sort field: '{fieldName}'.");

        if (!mapping.IsSortable)
            throw Invalid("sort", $"Sorting is not allowed on field '{fieldName}'.");

        return mapping.EntityExpression;
    }

    private static ValidationException Invalid(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });

    /// <summary>
    /// Checks whether a given field name is known in this map.
    /// </summary>
    /// <param name="fieldName">The field name to check.</param>
    /// <returns>True if the field is mapped; otherwise false.</returns>
    /// <remarks>
    /// Only declared fields count: a dynamic field exists for a request, not for the map, and is asked
    /// for through the filter and the sort.
    /// </remarks>
    public bool Contains(string fieldName) => _fields.ContainsKey(fieldName);

    /// <summary>
    /// Checks whether a field may be sorted on, without throwing the way
    /// <see cref="GetSortField"/> does.
    /// </summary>
    /// <remarks>
    /// Startup validation asks this about a configuration's default sort field. It needs the answer
    /// as a decision, not as an exception, because it reports every misconfiguration it finds in one
    /// message rather than stopping at the first.
    /// </remarks>
    /// <param name="fieldName">The field name to check.</param>
    /// <returns>True if the field is mapped and sortable; otherwise false.</returns>
    public bool IsSortable(string fieldName) =>
        _fields.TryGetValue(fieldName, out var mapping) && mapping.IsSortable;
}
