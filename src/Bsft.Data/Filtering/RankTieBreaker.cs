using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;

namespace Bsft.Data.Filtering;

/// <summary>
/// A single secondary sort applied after the score on the ranked search path: a key selector written
/// against the entity and the direction it is ordered in.
/// </summary>
/// <param name="Selector">The key selector, written against the entity.</param>
/// <param name="Direction">The direction the selector is ordered in.</param>
internal sealed record RankTieBreaker(LambdaExpression Selector, SortDirection Direction);
