using System.Linq.Expressions;

namespace Bsft.Data;

/// <summary>
/// One indexed column participating in a search group.
/// </summary>
/// <param name="Name">The entity property name backing the column.</param>
/// <param name="Selector">A lambda selecting the column from the entity.</param>
/// <param name="Weight">The relative weight of a match in this field.</param>
public sealed record SearchField(string Name, LambdaExpression Selector, double Weight);
