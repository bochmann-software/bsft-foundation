using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// Provides extension methods for projecting <see cref="IQueryable"/> sources
/// using an <see cref="IMapper"/> to build expression trees suitable for EF Core.
/// </summary>
public static class ProjectToExtensions
{
    /// <summary>How many type arguments an unindexed selector's delegate carries: source and result.</summary>
    private const int UnindexedSelectorArity = 2;

    private static readonly MethodInfo GetProjectionMethod =
        typeof(IMapper).GetMethod(nameof(IMapper.GetProjection))!;

    private static readonly MethodInfo SelectMethod = UnindexedSelect();

    /// <summary>
    /// Projects the source queryable to <typeparamref name="TDest"/> using the mapper's
    /// expression-tree projection, enabling server-side evaluation in EF Core.
    /// </summary>
    /// <typeparam name="TDest">The destination type to project to.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="mapper">The mapper providing the projection expression.</param>
    /// <returns>An <see cref="IQueryable{TDest}"/> with the projection applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IQueryable<TDest> ProjectTo<TDest>(this IQueryable source, IMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapper);

        var sourceElementType = source.ElementType;

        var projection = GetProjectionMethod
            .MakeGenericMethod(sourceElementType, typeof(TDest))
            .Invoke(mapper, parameters: null)!;

        var select = SelectMethod.MakeGenericMethod(sourceElementType, typeof(TDest));

        return (IQueryable<TDest>)select.Invoke(obj: null, [source, projection])!;
    }

    /// <summary>Reads the <c>Select</c> overload that takes a selector of the element alone.</summary>
    /// <returns>The open generic method.</returns>
    /// <remarks>
    /// Two overloads take two parameters and two type arguments; the other one's selector also takes
    /// the element's index. Telling them apart by parameter count alone left the choice to the order
    /// <see cref="Type.GetMethods()"/> happened to return them in, and the indexed one would have
    /// refused the projection it was handed.
    /// </remarks>
    private static MethodInfo UnindexedSelect()
        => typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Queryable.Select)
                && method.GetParameters().Length == 2
                && SelectorArity(method.GetParameters()[1]) == UnindexedSelectorArity);

    /// <summary>Counts the type arguments of the delegate a selector parameter wraps.</summary>
    /// <param name="selector">The selector parameter, an <see cref="System.Linq.Expressions.Expression{TDelegate}"/>.</param>
    /// <returns>The delegate's type-argument count.</returns>
    private static int SelectorArity(ParameterInfo selector)
        => selector.ParameterType.GetGenericArguments()[0].GetGenericArguments().Length;
}
