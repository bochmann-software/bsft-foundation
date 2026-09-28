using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Data.Filtering;

/// <summary>
/// An entity's key as both a selector to order by and the member it reads, so a caller can order by
/// it and tell whether a sort already does.
/// </summary>
/// <param name="Selector">A lambda selecting the key property of the entity.</param>
/// <param name="Member">The property the selector reads.</param>
internal sealed record EntityKeyDescriptor(LambdaExpression Selector, MemberInfo Member);
