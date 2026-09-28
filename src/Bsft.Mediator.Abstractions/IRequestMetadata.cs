namespace Bsft.Mediator;

/// <summary>
/// The concrete type of a dispatched request and the attributes declared on it, resolved once per
/// request type and reused for every later dispatch of that type.
/// </summary>
/// <remarks>
/// A pipeline behavior is closed over the declared contract (<see cref="ICommand{TResult}"/> or
/// <see cref="IQuery{TResult}"/>), never over the concrete request class, so an attribute placed on
/// the request is invisible to the behavior's type parameter. Reading it means reflecting on the
/// runtime type on every dispatch, which is why every attribute-driven behavior grew a per-type
/// cache of its own. This descriptor is that cache, provided once by the framework: obtain it from
/// <see cref="IRequestMetadataProvider"/> and read the attribute off it.
/// <para>
/// Implementations are immutable and safe to share across threads.
/// </para>
/// </remarks>
public interface IRequestMetadata
{
    /// <summary>
    /// Gets the concrete runtime type of the request — the request class itself, not the
    /// <see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/> contract it was dispatched under.
    /// </summary>
    Type RequestType { get; }

    /// <summary>
    /// Returns the single attribute of the given type declared on the request, or <c>null</c> when the
    /// request carries none.
    /// </summary>
    /// <typeparam name="TAttribute">The attribute type to look for.</typeparam>
    /// <returns>The first matching attribute, or <c>null</c>.</returns>
    /// <remarks>
    /// Where an attribute is applied more than once, one of them is returned; use
    /// <see cref="ListAttributes{TAttribute}"/> to read them all.
    /// </remarks>
    TAttribute? FindAttribute<TAttribute>() where TAttribute : Attribute;

    /// <summary>
    /// Returns every attribute of the given type declared on the request.
    /// </summary>
    /// <typeparam name="TAttribute">The attribute type to look for.</typeparam>
    /// <returns>The matching attributes, empty when the request carries none.</returns>
    /// <remarks>
    /// The order is the one reflection reports for the type and is not part of this contract; a
    /// behavior that has to distinguish several applications of the same attribute reads a property
    /// on it rather than relying on position.
    /// </remarks>
    IReadOnlyList<TAttribute> ListAttributes<TAttribute>() where TAttribute : Attribute;

    /// <summary>
    /// Indicates whether the request declares an attribute of the given type.
    /// </summary>
    /// <typeparam name="TAttribute">The attribute type to look for.</typeparam>
    /// <returns><c>true</c> when at least one such attribute is declared.</returns>
    bool HasAttribute<TAttribute>() where TAttribute : Attribute;
}
