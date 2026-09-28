using System.Linq.Expressions;

namespace Bsft.Mapping;

/// <summary>
/// Fluent builder for configuring how properties of <typeparamref name="TSource"/>
/// are mapped to properties of <typeparamref name="TDest"/>.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDest">The destination type.</typeparam>
public sealed class MapBuilder<TSource, TDest> : IMapBuilder
{
    private readonly Dictionary<string, LambdaExpression> _customMappings = new();
    private readonly HashSet<string> _ignoredMembers = new();

    /// <inheritdoc />
    IReadOnlyDictionary<string, LambdaExpression> IMapBuilder.CustomMappings => _customMappings;

    /// <inheritdoc />
    IReadOnlySet<string> IMapBuilder.IgnoredMembers => _ignoredMembers;

    /// <summary>
    /// Configures a custom mapping for the specified destination member.
    /// </summary>
    /// <typeparam name="TMember">The type of the destination member.</typeparam>
    /// <param name="destination">An expression selecting the destination member.</param>
    /// <param name="opts">An action that configures the mapping source for this member.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public MapBuilder<TSource, TDest> ForMember<TMember>(
        Expression<Func<TDest, TMember>> destination,
        Action<MemberMapOptions<TSource, TMember>> opts)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(opts);

        var memberName = GetMemberName(destination);
        var options = new MemberMapOptions<TSource, TMember>();
        opts(options);

        if (options.SourceExpression is null)
            throw new InvalidOperationException(
                $"MapFrom must be called when configuring member '{memberName}'.");

        _customMappings[memberName] = options.SourceExpression;
        return this;
    }

    /// <summary>
    /// Marks the specified destination member as intentionally unmapped.
    /// Ignored members are not auto-mapped by convention and are not flagged during validation.
    /// </summary>
    /// <typeparam name="TMember">The type of the destination member.</typeparam>
    /// <param name="destination">An expression selecting the destination member to ignore.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public MapBuilder<TSource, TDest> Ignore<TMember>(Expression<Func<TDest, TMember>> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var memberName = GetMemberName(destination);
        _ignoredMembers.Add(memberName);
        return this;
    }

    private static string GetMemberName<TMember>(Expression<Func<TDest, TMember>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
            return memberExpression.Member.Name;

        throw new ArgumentException(
            "Expression must be a simple member access expression (e.g., dest => dest.PropertyName).",
            nameof(expression));
    }
}
