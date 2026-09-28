using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Transactions;

/// <summary>
/// Locates the concrete <c>DbContext</c> that <see cref="TransactionBehavior{TRequest, TResponse}"/>
/// opens its transaction on, and reports when the container leaves that choice ambiguous. One seam so
/// the runtime resolution and the startup check share the rule about which context a
/// <c>[Transactional]</c> handler runs against.
/// </summary>
internal interface ITransactionDbContextResolver
{
    /// <summary>
    /// Resolves the concrete <c>DbContext</c> instance the transaction should run on, or
    /// <see langword="null"/> when EF Core is not loaded or no context is registered.
    /// </summary>
    /// <param name="serviceProvider">The scope's provider, used to resolve the context instance.</param>
    /// <returns>The resolved <c>DbContext</c>, or <see langword="null"/> when none is available.</returns>
    object? Resolve(IServiceProvider serviceProvider);

    /// <summary>
    /// The concrete context types the container has a <c>DbContextOptions&lt;T&gt;</c> registration for
    /// that the fallback could silently pick between — more than one, with no explicit base
    /// <c>DbContext</c> registration to disambiguate. Empty when the choice is unambiguous or EF Core
    /// is not loaded.
    /// </summary>
    /// <param name="services">The live service collection to inspect.</param>
    /// <returns>The competing context types when ambiguous; an empty list otherwise.</returns>
    IReadOnlyList<Type> ListAmbiguousContexts(IServiceCollection services);
}
