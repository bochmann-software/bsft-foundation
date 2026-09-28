using System.Linq.Expressions;
using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// Builds LINQ expression-tree projections from a <see cref="TypePairMapping"/>.
/// Used by <see cref="Bsft.Mapping.Mapper"/> to support EF Core IQueryable queries.
/// </summary>
internal static class MapperProjections
{
    /// <summary>
    /// Builds a compiled projection expression for the given mapping.
    /// </summary>
    internal static Expression<Func<TSource, TDest>> Build<TSource, TDest>(TypePairMapping mapping)
    {
        var sourceParam = Expression.Parameter(typeof(TSource), "src");
        var destType = typeof(TDest);

        var parameterlessCtor = destType.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

        if (parameterlessCtor is not null)
        {
            var bindings = BuildMemberBindings(mapping, sourceParam);
            var newExpr = Expression.New(destType);
            var memberInit = Expression.MemberInit(newExpr, bindings);
            return Expression.Lambda<Func<TSource, TDest>>(memberInit, sourceParam);
        }

        return BuildViaConstructor<TSource, TDest>(mapping, sourceParam, destType);
    }

    private static Expression<Func<TSource, TDest>> BuildViaConstructor<TSource, TDest>(
        TypePairMapping mapping, ParameterExpression sourceParameter, Type destinationType)
    {
        foreach (var candidate in mapping.Constructors)
        {
            var parameters = candidate.Parameters;
            if (!TryResolveArgumentExpressions(mapping, sourceParameter, parameters, out var argExpressions, out var usedProperties))
                continue;

            var newExpr = BuildConstructorCall(candidate.Constructor, argExpressions, parameters, destinationType);

            var additionalBindings = BuildMemberBindings(mapping, sourceParameter)
                .Where(b => !usedProperties.Contains(b.Member.Name))
                .ToList();

            if (additionalBindings.Count > 0)
            {
                var memberInit = Expression.MemberInit(newExpr, additionalBindings);
                return Expression.Lambda<Func<TSource, TDest>>(memberInit, sourceParameter);
            }

            return Expression.Lambda<Func<TSource, TDest>>(newExpr, sourceParameter);
        }

        throw new InvalidOperationException(
            $"Cannot build projection for '{destinationType.Name}'. No suitable constructor found.");
    }

    /// <summary>Builds the construction, naming the property behind each argument where it can.</summary>
    /// <param name="constructor">The constructor to call.</param>
    /// <param name="argumentExpressions">The resolved argument expressions.</param>
    /// <param name="parameters">The constructor's parameters.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <returns>The construction expression.</returns>
    /// <remarks>
    /// The members overload is what lets a query provider read a constructor argument back as the
    /// property it fills, so it is used whenever every parameter has a property of its name. A
    /// parameter resolved from its own default value need not, and passing a null member for it
    /// would fail the construction outright rather than lose the naming.
    /// </remarks>
    private static NewExpression BuildConstructorCall(
        ConstructorInfo constructor,
        Expression[] argumentExpressions,
        IReadOnlyList<ParameterInfo> parameters,
        Type destinationType)
    {
        var members = new MemberInfo[parameters.Count];

        for (var index = 0; index < parameters.Count; index++)
        {
            var member = destinationType.GetProperty(
                parameters[index].Name!,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (member is null)
                return Expression.New(constructor, argumentExpressions);

            members[index] = member;
        }

        return Expression.New(constructor, argumentExpressions, members);
    }

    private static bool TryResolveArgumentExpressions(
        TypePairMapping mapping,
        ParameterExpression sourceParameter,
        IReadOnlyList<ParameterInfo> parameters,
        out Expression[] argumentExpressions,
        out HashSet<string> usedProperties)
    {
        argumentExpressions = new Expression[parameters.Count];
        usedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < parameters.Count; i++)
        {
            var paramName = parameters[i].Name!;
            var argExpr = ResolveParameterExpression(mapping, sourceParameter, paramName, parameters[i].ParameterType);

            if (argExpr is not null)
            {
                argumentExpressions[i] = argExpr;
                usedProperties.Add(paramName);
            }
            else if (parameters[i].HasDefaultValue)
            {
                argumentExpressions[i] = Expression.Constant(parameters[i].DefaultValue, parameters[i].ParameterType);
                usedProperties.Add(paramName);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static Expression? ResolveParameterExpression(
        TypePairMapping mapping, ParameterExpression sourceParameter, string parameterName, Type targetType)
    {
        foreach (var (destProp, sourceProp) in mapping.ConventionMappings)
        {
            if (!string.Equals(destProp.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                continue;

            Expression access = Expression.Property(sourceParameter, sourceProp);
            if (access.Type != targetType)
                access = Expression.Convert(access, targetType);
            return access;
        }

        foreach (var kvp in mapping.CustomMappings)
        {
            if (!string.Equals(kvp.Key, parameterName, StringComparison.OrdinalIgnoreCase))
                continue;

            var sourceExpr = kvp.Value;
            var reboundBody = ReplaceParameter(sourceExpr.Body, sourceExpr.Parameters[0], sourceParameter);
            if (reboundBody.Type != targetType)
                reboundBody = Expression.Convert(reboundBody, targetType);
            return reboundBody;
        }

        return null;
    }

    /// <summary>Binds every mapped member the destination lets a projection assign.</summary>
    /// <param name="mapping">The mapping for the type pair.</param>
    /// <param name="sourceParameter">The projection's source parameter.</param>
    /// <returns>The bindings.</returns>
    internal static List<MemberBinding> BuildMemberBindings(
        TypePairMapping mapping, ParameterExpression sourceParameter)
    {
        var bindings = new List<MemberBinding>();

        foreach (var (destProp, sourceProp) in mapping.ConventionMappings)
        {
            // Expression.Bind refuses a property it cannot assign, so a destination member the
            // in-memory map quietly skips has to be skipped here too rather than throw while the
            // projection is built.
            if (!DestinationProperties.IsSettable(destProp))
                continue;

            var sourceAccess = Expression.Property(sourceParameter, sourceProp);
            bindings.Add(Expression.Bind(destProp, sourceAccess));
        }

        foreach (var kvp in mapping.CustomMappings)
        {
            if (mapping.CustomTargets[kvp.Key] is not { } destProp)
                continue;

            var sourceExpr = kvp.Value;
            var reboundBody = ReplaceParameter(sourceExpr.Body, sourceExpr.Parameters[0], sourceParameter);
            bindings.Add(Expression.Bind(destProp, reboundBody));
        }

        return bindings;
    }

    private static Expression ReplaceParameter(
        Expression expression, ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        return new ParameterReplacer(oldParameter, newParameter).Visit(expression);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParam;
        private readonly ParameterExpression _newParam;

        internal ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParam = oldParameter;
            _newParam = newParameter;
        }

        /// <inheritdoc/>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParam ? _newParam : base.VisitParameter(node);
        }
    }
}
