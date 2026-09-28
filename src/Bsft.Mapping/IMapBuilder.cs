using System.Linq.Expressions;

namespace Bsft.Mapping;

/// <summary>
/// What a <see cref="MapBuilder{TSource, TDest}"/> carries once its type arguments no longer matter.
/// </summary>
/// <remarks>
/// A profile registers builders of many different type pairs, so they can only be held together
/// without their type arguments. This is the seam that keeps that from meaning <see cref="object"/>
/// and a reflective read of two property names.
/// </remarks>
internal interface IMapBuilder
{
    /// <summary>Gets the custom member mappings keyed by destination property name.</summary>
    IReadOnlyDictionary<string, LambdaExpression> CustomMappings { get; }

    /// <summary>Gets the set of destination property names that are explicitly ignored.</summary>
    IReadOnlySet<string> IgnoredMembers { get; }
}
