using Bsft.Data.Contracts.Search;
using System.Linq.Expressions;

namespace Bsft.Data;

/// <summary>
/// Fluent API for declaring a search group on an entity.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class SearchGroupBuilder<TEntity>
{
    private readonly string _name;
    private readonly List<SearchField> _fields = [];
    private readonly HashSet<string> _fieldNames = new(StringComparer.Ordinal);
    private SearchMode _mode = SearchMode.Prefix;
    private string _language = "en";
    private bool _isUnscoped;

    internal SearchGroupBuilder(string name) => _name = name;

    /// <summary>
    /// Adds an indexed column to the group.
    /// </summary>
    /// <param name="property">An expression selecting the entity column. The column, not a DTO
    /// property: a full-text index exists on a column, and naming it here keeps the declaration
    /// honest about what has to be indexed.</param>
    /// <param name="weight">The relative weight of a match in this field. Defaults to 1.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="property"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the expression is not a simple property access.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="weight"/> is not positive.</exception>
    public SearchGroupBuilder<TEntity> Field(Expression<Func<TEntity, string?>> property, double weight = 1.0)
    {
        if (property is null)
            throw new ArgumentNullException(nameof(property));
        if (weight <= 0)
            throw new ArgumentOutOfRangeException(nameof(weight), "A field weight must be positive.");

        if (property.Body is not MemberExpression member)
            throw new ArgumentException("A searchable field must be a simple property access.", nameof(property));

        // Declaring the same column twice would silently double its contribution to the relevance
        // score, which is indistinguishable from a deliberately higher weight.
        if (!_fieldNames.Add(member.Member.Name))
            throw new ArgumentException($"Field '{member.Member.Name}' is already declared in this search group.", nameof(property));

        _fields.Add(new SearchField(member.Member.Name, property, weight));
        return this;
    }

    /// <summary>
    /// Sets the mode used when a request does not specify one.
    /// </summary>
    /// <param name="mode">The default search mode.</param>
    /// <returns>The same builder for chaining.</returns>
    public SearchGroupBuilder<TEntity> Mode(SearchMode mode)
    {
        _mode = mode;
        return this;
    }

    /// <summary>
    /// Sets the stemming language of the index.
    /// </summary>
    /// <param name="language">A two-letter language code such as <c>de</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="language"/> is null or whitespace.</exception>
    public SearchGroupBuilder<TEntity> Language(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("A language code is required.", nameof(language));

        _language = language;
        return this;
    }

    /// <summary>
    /// Declares that this group is searched without a tenant or organisational boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required for an entity that carries no scoping marker. Making it explicit is the point: a
    /// forgotten <c>ITenantEntity</c> or <c>IOrganizationScopedEntity</c> would otherwise look exactly like
    /// data that is meant to be visible to everyone, and the difference is a data leak.
    /// </para>
    /// <para>
    /// It is <em>only</em> accepted there. On an entity that does carry a marker, startup validation
    /// refuses the declaration rather than letting one builder call switch off the boundary that
    /// entity exists to have. Data that has a tenant is searched within it; a genuinely
    /// boundary-free group belongs on an entity that has no boundary to begin with.
    /// </para>
    /// </remarks>
    /// <returns>The same builder for chaining.</returns>
    public SearchGroupBuilder<TEntity> Unscoped()
    {
        _isUnscoped = true;
        return this;
    }

    /// <summary>
    /// Builds the declared search group.
    /// </summary>
    /// <returns>The search group.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the group declares no fields.</exception>
    public SearchGroupConfiguration Build()
    {
        if (_fields.Count == 0)
            throw new InvalidOperationException($"Search group '{_name}' declares no fields.");

        return new SearchGroupConfiguration(_name, typeof(TEntity), _fields, _mode, _language, _isUnscoped);
    }
}
