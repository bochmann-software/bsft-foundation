using System.Linq.Expressions;

namespace Bsft.Mapping;

/// <summary>
/// Provides options for configuring how a specific destination member is mapped from the source type.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TMember">The type of the destination member being configured.</typeparam>
/// <remarks>
/// Despite the name this is not an options type in the sense the package conventions use: it is the
/// value a profile's fluent call hands back while a map is being described, it is never registered in
/// the container and it binds from no configuration section, so it carries no <c>SectionName</c> and
/// no validator. A profile is code, and a mapping cannot be described in a JSON section.
/// </remarks>
public sealed class MemberMapOptions<TSource, TMember>
{
    /// <summary>
    /// Gets the source expression that was configured, or null if not yet configured.
    /// </summary>
    internal Expression<Func<TSource, TMember>>? SourceExpression { get; private set; }

    /// <summary>
    /// Specifies the source expression to use when mapping this destination member.
    /// </summary>
    /// <param name="sourceExpression">An expression that extracts the value from the source object.</param>
    /// <returns>This options instance for method chaining.</returns>
    public MemberMapOptions<TSource, TMember> MapFrom(Expression<Func<TSource, TMember>> sourceExpression)
    {
        ArgumentNullException.ThrowIfNull(sourceExpression);

        SourceExpression = sourceExpression;
        return this;
    }
}
