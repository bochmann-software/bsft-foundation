using System.Linq.Expressions;


namespace Bsft.Data.Filtering;

/// <summary>
/// Rebinds a field-map's stored entity lambda onto the query's own parameter. The filter compiler resolves every field through here, so the mechanics of turning a
/// declared <c>e =&gt; e.Property</c> mapping into a member access on the compiled predicate's parameter
/// live in one focused place rather than spread through the compiler.
/// </summary>
internal static class EntityMemberBinder
{
    /// <summary>
    /// Rebinds the body of <paramref name="sourceExpression"/> from its own parameter onto
    /// <paramref name="newParameter"/> and strips the outer <c>object</c> boxing convention adds, so the
    /// result is a member access typed as the entity property itself.
    /// </summary>
    /// <param name="sourceExpression">The field-map lambda selecting the entity property.</param>
    /// <param name="newParameter">The parameter of the predicate being compiled.</param>
    /// <returns>The member access expression bound to <paramref name="newParameter"/>.</returns>
    public static Expression Bind(LambdaExpression sourceExpression, ParameterExpression newParameter)
    {
        var visitor = new ParameterReplacer(sourceExpression.Parameters[0], newParameter);
        return StripConvert(visitor.Visit(sourceExpression.Body));
    }

    private static Expression StripConvert(Expression expression)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            return unary.Operand;

        return expression;
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly ParameterExpression _newParameter;

        public ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParameter = oldParameter;
            _newParameter = newParameter;
        }

        /// <inheritdoc/>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParameter ? _newParameter : base.VisitParameter(node);
        }
    }
}
