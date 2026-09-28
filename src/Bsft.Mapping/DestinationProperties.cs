using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// The one rule deciding which destination properties the mapper is allowed to write.
/// </summary>
internal static class DestinationProperties
{
    /// <summary>Reports whether the mapper may assign to a destination property.</summary>
    /// <param name="property">The destination property.</param>
    /// <returns><see langword="true"/> when the property has a public setter.</returns>
    /// <remarks>
    /// <para>
    /// A property counts as public as soon as its getter is, so <c>{ get; private set; }</c> arrives
    /// here with <see cref="PropertyInfo.CanWrite"/> set and
    /// <see cref="PropertyInfo.SetValue(object?, object?)"/> willing to call the private setter.
    /// Mapping onto an existing instance would then overwrite
    /// exactly the members an entity made unsettable on purpose — its identifier, its owner, its
    /// tenant — from whatever DTO happened to carry a same-named property. Only a setter the caller
    /// could have called itself is one the mapper may call on its behalf.
    /// </para>
    /// <para>
    /// The same rule decides what a projection binds, because <c>Expression.Bind</c> refuses a
    /// property it cannot assign and would otherwise throw while the in-memory map quietly skipped it.
    /// An init-only setter is public and is therefore assignable here, which is what lets a record be
    /// both constructed and projected.
    /// </para>
    /// </remarks>
    internal static bool IsSettable(PropertyInfo property) => property.GetSetMethod(nonPublic: false) is not null;
}
