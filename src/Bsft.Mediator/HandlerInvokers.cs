using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Bsft.Mediator;

/// <summary>
/// Resolves, once per request type, the closed handler interface a request dispatches to and a
/// compiled call to its <c>HandleAsync</c>.
/// </summary>
/// <remarks>
/// Dispatch is the hottest path the mediator has, and what a request type dispatches to is fixed
/// once the application's assemblies are loaded. Calling <c>HandleAsync</c> reflectively would also
/// wrap whatever a handler throws before returning its task in a
/// <see cref="System.Reflection.TargetInvocationException"/>, so a handler that guards its arguments
/// synchronously would reach the caller, and the behaviors logging it, as something else entirely.
/// </remarks>
internal static class HandlerInvokers
{
    private static readonly ConcurrentDictionary<(Type Definition, Type Request, Type? Result), Type> ClosedTypes = new();
    private static readonly ConcurrentDictionary<Type, Delegate> Invokers = new();

    /// <summary>Closes a handler interface over the request type it handles.</summary>
    /// <param name="definition">The open handler interface.</param>
    /// <param name="requestType">The request type's runtime type.</param>
    /// <returns>The closed handler interface.</returns>
    internal static Type Close(Type definition, Type requestType)
        => ClosedTypes.GetOrAdd(
            (definition, requestType, null),
            static key => key.Definition.MakeGenericType(key.Request));

    /// <summary>Closes a handler interface over the request type it handles and the result it returns.</summary>
    /// <param name="definition">The open handler interface.</param>
    /// <param name="requestType">The request type's runtime type.</param>
    /// <param name="resultType">The result type.</param>
    /// <returns>The closed handler interface.</returns>
    internal static Type Close(Type definition, Type requestType, Type resultType)
        => ClosedTypes.GetOrAdd(
            (definition, requestType, resultType),
            static key => key.Definition.MakeGenericType(key.Request, key.Result!));

    /// <summary>Returns the compiled call to one closed handler interface's <c>HandleAsync</c>.</summary>
    /// <typeparam name="TTask">
    /// The task <c>HandleAsync</c> returns: <see cref="Task"/> for a handler without a result,
    /// <c>Task&lt;TResult&gt;</c> otherwise.
    /// </typeparam>
    /// <param name="handlerType">The closed handler interface.</param>
    /// <returns>The compiled call.</returns>
    internal static Func<object, object, CancellationToken, TTask> Of<TTask>(Type handlerType)
        where TTask : Task
        => (Func<object, object, CancellationToken, TTask>)Invokers.GetOrAdd(handlerType, static type => Compile<TTask>(type));

    /// <summary>Compiles the call to one closed handler interface's <c>HandleAsync</c>.</summary>
    /// <typeparam name="TTask">The task <c>HandleAsync</c> returns.</typeparam>
    /// <param name="handlerType">The closed handler interface.</param>
    /// <returns>The compiled call.</returns>
    private static Func<object, object, CancellationToken, TTask> Compile<TTask>(Type handlerType)
        where TTask : Task
    {
        var method = handlerType.GetMethod("HandleAsync")!;
        var handler = Expression.Parameter(typeof(object), "handler");
        var request = Expression.Parameter(typeof(object), "request");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var call = Expression.Call(
            Expression.Convert(handler, handlerType),
            method,
            Expression.Convert(request, method.GetParameters()[0].ParameterType),
            cancellationToken);

        return Expression.Lambda<Func<object, object, CancellationToken, TTask>>(
            call, handler, request, cancellationToken).Compile();
    }
}
