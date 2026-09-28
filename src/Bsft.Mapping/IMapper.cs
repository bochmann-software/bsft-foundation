using System.Linq.Expressions;

namespace Bsft.Mapping;

/// <summary>
/// Defines a convention-based object mapper that supports both in-memory mapping
/// and expression-tree projection for use with IQueryable providers such as EF Core.
/// </summary>
public interface IMapper
{
    /// <summary>
    /// Maps the specified source object to a new instance of <typeparamref name="TDest"/>
    /// using the runtime type of <paramref name="source"/> to resolve the mapping configuration.
    /// </summary>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="source">The source object to map from.</param>
    /// <returns>A new instance of <typeparamref name="TDest"/> with mapped property values.</returns>
    TDest Map<TDest>(object source);

    /// <summary>
    /// Maps the specified source object to a new instance of <typeparamref name="TDest"/>.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="source">The source object to map from.</param>
    /// <returns>A new instance of <typeparamref name="TDest"/> with mapped property values.</returns>
    TDest Map<TSource, TDest>(TSource source);

    /// <summary>
    /// Maps the specified source object into an existing instance of <typeparamref name="TDest"/>,
    /// updating its properties in place.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="source">The source object to map from.</param>
    /// <param name="existing">The existing destination instance to map into.</param>
    /// <returns>The same <paramref name="existing"/> instance with updated property values.</returns>
    TDest Map<TSource, TDest>(TSource source, TDest existing);

    /// <summary>
    /// Returns an expression tree that projects <typeparamref name="TSource"/> to <typeparamref name="TDest"/>
    /// using MemberInit expressions, suitable for use with IQueryable providers such as EF Core.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <returns>An expression that can be passed to <c>IQueryable.Select()</c>.</returns>
    Expression<Func<TSource, TDest>> GetProjection<TSource, TDest>();
}
