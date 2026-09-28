using System.Collections.Concurrent;
using System.Reflection;

namespace Bsft.Mediator.Transactions;

/// <summary>
/// Decides whether the handler of a request declares itself transactional.
/// </summary>
/// <remarks>
/// Kept apart from the behavior that opens the transaction: deciding what needs one is a question
/// about the application's types, answered once per request type by scanning them, while the
/// behavior's own work is the transaction's lifetime around a single dispatch.
/// </remarks>
internal static class TransactionalHandlers
{
    /// <summary>
    /// The handler interfaces a request may be handled through. The resultless command handler is on
    /// the list too: a handler that returns nothing is no less a write, and leaving it off made
    /// <c>[Transactional]</c> silently inert on exactly the operations - delete, remove - whose
    /// partial completion is hardest to notice.
    /// </summary>
    private static readonly Type[] HandlerInterfaces =
        [typeof(ICommandHandler<,>), typeof(ICommandHandler<>), typeof(IQueryHandler<,>)];

    private static readonly ConcurrentDictionary<Type, bool> RequiresTransaction = new();

    /// <summary>Reports whether a request type is handled by a type marked transactional.</summary>
    /// <param name="requestType">The request's runtime type.</param>
    /// <returns><see langword="true"/> when the dispatch belongs in a transaction.</returns>
    internal static bool IsRequired(Type requestType) => RequiresTransaction.GetOrAdd(requestType, IsHandledTransactionally);

    /// <summary>Scans the loaded assemblies for a transactional handler of one request type.</summary>
    /// <param name="requestType">The request's runtime type.</param>
    /// <returns><see langword="true"/> when one was found.</returns>
    private static bool IsHandledTransactionally(Type requestType)
        => AppDomain.CurrentDomain.GetAssemblies().Any(assembly => IsHandledBy(assembly, requestType));

    /// <summary>Reports whether one assembly carries a transactional handler of a request type.</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="requestType">The request's runtime type.</param>
    /// <returns><see langword="true"/> when one was found.</returns>
    private static bool IsHandledBy(Assembly assembly, Type requestType)
        => AssemblyTypes.Loadable(assembly).Any(type =>
            type.IsClass
            && !type.IsAbstract
            && type.GetCustomAttribute<TransactionalAttribute>() is not null
            && IsHandlerFor(type, requestType));

    /// <summary>Reports whether one type handles a request type.</summary>
    /// <param name="type">The candidate handler.</param>
    /// <param name="requestType">The request's runtime type.</param>
    /// <returns><see langword="true"/> when the type implements a handler interface over the request.</returns>
    private static bool IsHandlerFor(Type type, Type requestType)
        => type.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && HandlerInterfaces.Contains(contract.GetGenericTypeDefinition())
            && contract.GetGenericArguments()[0] == requestType);
}
