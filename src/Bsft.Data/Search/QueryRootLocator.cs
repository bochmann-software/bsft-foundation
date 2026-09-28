using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Bsft.Data.Search;

/// <summary>
/// Finds the EF Core model behind a queryable by reading it off the query's own root.
/// </summary>
/// <remarks>
/// <para>
/// A search provider needs the model to check what it is about to compose against — the SQLite one
/// reaches a second table, the FTS5 index, that the caller's queryable does not mention at all.
/// Taking the model from the source rather than from an injected <c>DbContext</c> is what keeps a
/// provider a singleton, and it removes a whole class of mistake: anything built from it belongs to
/// the caller's own query provider and cannot accidentally come from a different context than the
/// query it is composed into.
/// </para>
/// <para>
/// A queryable that is not EF Core's — a list, or a stub in a test — has no root and is reported as
/// such rather than silently searching nothing.
/// </para>
/// </remarks>
internal static class QueryRootLocator
{
    /// <summary>
    /// Finds the model behind a query expression.
    /// </summary>
    /// <param name="expression">The query expression.</param>
    /// <returns>The model, or <see langword="null"/> when the expression is not rooted in an EF Core set.</returns>
    public static IModel? FindModel(Expression expression)
    {
        var visitor = new RootVisitor();
        visitor.Visit(expression);

        return visitor.Root?.EntityType.Model;
    }

    private sealed class RootVisitor : ExpressionVisitor
    {
        public EntityQueryRootExpression? Root { get; private set; }

        /// <inheritdoc/>
        public override Expression? Visit(Expression? node)
        {
            if (Root is not null)
                return node;

            if (node is EntityQueryRootExpression root)
            {
                Root = root;
                return node;
            }

            return base.Visit(node);
        }
    }
}
