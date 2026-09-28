using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Data.Filtering;

/// <summary>
/// Resolves an entity's unique key by convention, so paging can append it as the last-resort
/// tie-breaker. Every store in the framework hand-writes the same <c>ThenBy(e =&gt; e.Id)</c>; this is
/// that rule made available to the generic query pipeline, where the entity type is only known at
/// runtime.
/// </summary>
/// <remarks>
/// The convention is the one EF Core itself uses for a primary key: a public instance property named
/// <c>Id</c>, or <c>&lt;EntityName&gt;Id</c>. A property whose type cannot be ordered — a navigation
/// property, for instance — is not a key for this purpose and is ignored, which leaves the caller
/// with its own fallback rather than with a query the provider would refuse to translate.
/// </remarks>
internal static class EntityKeyResolver
{
    private static readonly ConcurrentDictionary<Type, EntityKeyDescriptor?> _cache = new();

    /// <summary>
    /// Gets the key selector for an entity type, or <see langword="null"/> when the type follows no
    /// key convention. The answer is cached per type because it is pure reflection over a closed type.
    /// </summary>
    /// <param name="entityType">The entity type whose key is sought.</param>
    /// <returns>The key selector, or <see langword="null"/> when there is none.</returns>
    public static EntityKeyDescriptor? Resolve(Type entityType) => _cache.GetOrAdd(entityType, Build);

    /// <summary>
    /// Gets the member a key selector body reads, so a caller can tell whether a sort already orders
    /// by that member.
    /// </summary>
    /// <param name="body">The expression body to inspect.</param>
    /// <returns>The member being read, or <see langword="null"/> when the body is not a member access.</returns>
    public static MemberInfo? FindMember(Expression body)
    {
        // A field map's selectors are built as `e => (object)e.Member`, so the conversion has to come
        // off before the member is visible.
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        return body is MemberExpression member ? member.Member : null;
    }

    private static EntityKeyDescriptor? Build(Type entityType)
    {
        if (FindKeyProperty(entityType) is not { } property)
            return null;

        var parameter = Expression.Parameter(entityType, "e");
        return new EntityKeyDescriptor(Expression.Lambda(Expression.Property(parameter, property), parameter), property);
    }

    private static PropertyInfo? FindKeyProperty(Type entityType) =>
        FindOrderableProperty(entityType, "Id") ?? FindOrderableProperty(entityType, entityType.Name + "Id");

    private static PropertyInfo? FindOrderableProperty(Type entityType, string name)
    {
        // A property hidden by `new` in a derived type would make the lookup ambiguous. That is a
        // modelling mistake, not a reason to fail a query, so it simply resolves to no key.
        PropertyInfo? property;
        try
        {
            property = entityType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }

        return property is not null && IsOrderable(property.PropertyType) ? property : null;
    }

    private static bool IsOrderable(Type type) =>
        typeof(IComparable).IsAssignableFrom(Nullable.GetUnderlyingType(type) ?? type);
}
