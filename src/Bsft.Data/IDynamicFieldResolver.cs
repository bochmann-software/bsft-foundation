namespace Bsft.Data;

/// <summary>
/// Resolves the fields under a dynamic prefix — fields no DTO property declares, such as a tenant's
/// configured extension fields — to what a filter or a sort may do with them.
/// </summary>
/// <typeparam name="TEntity">The entity the fields belong to.</typeparam>
/// <remarks>
/// <para>
/// Registered with <see cref="QueryConfigurationBuilder{TEntity, TDto}.DynamicFields"/>. The resolver
/// is asked on every request, for every filter node and sort item that names a field under its prefix,
/// so which fields exist and what they allow may depend on the request (the tenant, the caller's
/// rights). The configuration holding it lives for the application, so an implementation is
/// thread-safe and reads request state from an ambient accessor rather than from a captured scope.
/// </para>
/// <para>
/// A field the resolver does not know is answered with <see langword="null"/> and becomes the same
/// <c>400</c> an undeclared DTO field is, with no hint whether the field exists for someone else.
/// </para>
/// <para>
/// The key is client input, exactly as the request spelled it. Match it against the field definitions
/// the resolver knows and build the predicates from the matched definition; never put the key itself
/// into SQL text, a JSON path or any other expression a database parses. Where the key must reach the
/// query (a jsonb key test, say), pass it as a value, which EF Core sends as a parameter.
/// </para>
/// </remarks>
public interface IDynamicFieldResolver<TEntity>
{
    /// <summary>
    /// Resolves one field under the prefix.
    /// </summary>
    /// <param name="key">The field name with the prefix removed, exactly as the request spelled it.</param>
    /// <returns>What the field allows, or <see langword="null"/> when it is unknown to this request.</returns>
    DynamicField<TEntity>? Resolve(string key);
}
