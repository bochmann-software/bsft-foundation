namespace Bsft.Data.Search;

/// <summary>
/// Carries a provider's search expression so that it reaches SQL as a parameter rather than a literal.
/// </summary>
/// <remarks>
/// Entity Framework Core lifts a member access on a captured instance into a query parameter, while a
/// <see cref="System.Linq.Expressions.ConstantExpression"/> holding the string is written into the SQL
/// text. Parameterizing means distinct search terms produce one identical, cacheable statement, and it
/// is what keeps the term out of the query log. Shared by the three provider packages, which all need
/// the same box for the same reason; internal, so it is not a surface an application reaches.
/// </remarks>
internal sealed class SearchTerm
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchTerm"/> class.
    /// </summary>
    /// <param name="value">The expression to hand to the database.</param>
    public SearchTerm(string value) => Value = value;

    /// <summary>Gets the expression to hand to the database.</summary>
    public string Value { get; }
}
