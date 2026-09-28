using Bsft.Data.Contracts.Filtering;
using System.Linq.Expressions;

namespace Bsft.Data;

/// <summary>
/// Fluent builder for configuring a single field mapping in a query configuration.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDto">The DTO type.</typeparam>
public sealed class FieldBuilder<TEntity, TDto>
{
    private readonly string _dtoFieldName;
    private LambdaExpression? _entityExpression;
    private readonly HashSet<FilterOperator> _allowedOperators = [];
    private bool _sortable;

    /// <summary>
    /// Initializes a new instance of the <see cref="FieldBuilder{TEntity, TDto}"/> class.
    /// </summary>
    /// <param name="dtoFieldName">The DTO property name this builder configures.</param>
    /// <param name="defaultEntityExpression">An optional default entity expression derived from convention.</param>
    internal FieldBuilder(string dtoFieldName, LambdaExpression? defaultEntityExpression)
    {
        _dtoFieldName = dtoFieldName;
        _entityExpression = defaultEntityExpression;
    }

    /// <summary>
    /// Explicitly maps this DTO field to an entity property path.
    /// </summary>
    /// <param name="entityProperty">An expression selecting the entity property.</param>
    /// <returns>This builder for chaining.</returns>
    public FieldBuilder<TEntity, TDto> MapsTo(Expression<Func<TEntity, object?>> entityProperty)
    {
        _entityExpression = entityProperty ?? throw new ArgumentNullException(nameof(entityProperty));
        return this;
    }

    /// <summary>
    /// Specifies which filter operators are allowed on this field.
    /// </summary>
    /// <param name="operators">The allowed operators.</param>
    /// <returns>This builder for chaining.</returns>
    public FieldBuilder<TEntity, TDto> AllowFilter(params FilterOperator[] operators)
    {
        foreach (var op in operators)
            _allowedOperators.Add(op);

        return this;
    }

    /// <summary>
    /// Marks this field as sortable.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public FieldBuilder<TEntity, TDto> AllowSort()
    {
        _sortable = true;
        return this;
    }

    /// <summary>
    /// Builds the field mapping and applies it to the given field map.
    /// </summary>
    /// <param name="fieldMap">The target field map.</param>
    internal void Apply(QueryFieldMap<TEntity> fieldMap)
    {
        if (_entityExpression is null)
            throw new InvalidOperationException($"No entity mapping configured for DTO field '{_dtoFieldName}'. Use MapsTo() to specify the mapping.");

        fieldMap.Add(_dtoFieldName, _entityExpression, _allowedOperators, _sortable);
    }

    /// <summary>
    /// Returns this builder instance. Satisfies the builder contract.
    /// </summary>
    /// <returns>This builder instance.</returns>
    public FieldBuilder<TEntity, TDto> Build() => this;

    /// <summary>
    /// Gets the DTO field name.
    /// </summary>
    internal string DtoFieldName => _dtoFieldName;
}
