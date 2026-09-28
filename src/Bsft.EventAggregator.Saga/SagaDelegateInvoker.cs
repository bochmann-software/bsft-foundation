using System.Linq.Expressions;

namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Turns a saga step's or condition's two-argument delegate, whose parameter types are only known at
/// definition time, into one that takes both arguments as <see cref="object"/>.
/// </summary>
/// <remarks>
/// The alternative is <see cref="Delegate.DynamicInvoke"/> at every dispatch, which boxes its arguments
/// into a fresh array and walks reflection to find the target each time. A saga definition is built
/// once and then dispatched against for the life of the process, so the compile belongs on the
/// building side, next to the type resolution and the closed publish methods this package already
/// works out once.
/// </remarks>
internal static class SagaDelegateInvoker
{
    /// <summary>
    /// Compiles a caller for a delegate taking two arguments and returning <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">What the delegate returns.</typeparam>
    /// <param name="target">The delegate to call.</param>
    /// <returns>A function calling <paramref name="target"/> with both arguments as objects.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the delegate does not take exactly two arguments, or returns something
    /// <typeparamref name="TResult"/> cannot hold.
    /// </exception>
    public static Func<object?, object?, TResult> For<TResult>(Delegate target)
    {
        var parameters = target.Method.GetParameters();

        if (parameters.Length != 2)
        {
            throw new ArgumentException(
                $"A saga delegate takes an argument and the event, but this one takes {parameters.Length}.",
                nameof(target));
        }

        var first = Expression.Parameter(typeof(object), "first");
        var second = Expression.Parameter(typeof(object), "second");

        var call = Expression.Invoke(
            Expression.Constant(target),
            Expression.Convert(first, parameters[0].ParameterType),
            Expression.Convert(second, parameters[1].ParameterType));

        return Expression
            .Lambda<Func<object?, object?, TResult>>(Expression.Convert(call, typeof(TResult)), first, second)
            .Compile();
    }
}
