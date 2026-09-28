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

        if (mapping.AllowedOperators.Count > 0 && !mapping.AllowedOperators.Contains(op))
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
    /// </remarks>
    public LambdaExpression GetSortField(string fieldName)
    {
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
