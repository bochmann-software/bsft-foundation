using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Bsft.Mediator.Transactions;

/// <summary>
/// Locates the concrete <c>DbContext</c> that <see cref="TransactionBehavior{TRequest, TResponse}"/>
/// opens its transaction on, and reports when the container leaves that choice ambiguous.
/// </summary>
/// <remarks>
/// Splitting this out of <see cref="TransactionBehavior{TRequest, TResponse}"/> keeps the behavior to
/// the transaction lifecycle and gives the location logic one home: the runtime resolution the
/// behavior uses and the startup check the validator uses share the same rules about which context a
/// <c>[Transactional]</c> handler runs against, so the two cannot drift apart.
/// <para>
/// <c>AddDbContext&lt;TContext&gt;()</c> registers only the concrete <c>TContext</c> and the
/// <c>DbContextOptions&lt;TContext&gt;</c>, never the base <c>DbContext</c> service type. EF Core's
/// <c>TryAdd</c> also binds the non-generic <c>DbContextOptions</c> to whichever context was
/// registered first. Resolving by base fails under the standard registration, and resolving through
/// the non-generic <c>DbContextOptions.ContextType</c> is unambiguous only when a single context is
/// registered. With more than one context and no explicit base <c>DbContext</c> to disambiguate, the
/// fallback would silently pick the first-registered context and wrap a transaction on the wrong one,
/// so the startup validator fails fast on exactly that shape instead.
/// </para>
/// <para>
/// The resolved target service type is cached on this resolver, which is registered as an application
/// singleton, keyed by the non-generic <c>DbContextOptions</c> service type. That key is stable for the
/// application's lifetime, so the reflection that reads <c>ContextType</c> runs at most once per
/// application in the common case for both pooled and non-pooled registration — a concurrent cold-start
/// may compute it more than once, but the result settles to a single cached entry. Keying on the
/// options <em>instance</em> would not achieve
/// this on the common path: <c>AddDbContext&lt;T&gt;()</c> registers <c>DbContextOptions</c> as scoped,
/// so a fresh instance per DI scope (per request) would rebuild the entry and rerun the reflection every
/// request; <c>AddDbContextPool&lt;T&gt;()</c> alone hands out an application-lifetime instance. Caching
/// on the singleton by type sidesteps that: only the resolved <em>type</em> is cached, while the
/// concrete <c>DbContext</c> instance is still resolved from the request-scoped provider on every call.
/// </para>
/// </remarks>
internal sealed class TransactionDbContextResolver : ITransactionDbContextResolver
{
    private const string DbContextTypeName =
        "Microsoft.EntityFrameworkCore.DbContext, Microsoft.EntityFrameworkCore";
    private const string DbContextOptionsTypeName =
        "Microsoft.EntityFrameworkCore.DbContextOptions, Microsoft.EntityFrameworkCore";
    private const string DbContextOptionsOpenTypeName =
        "Microsoft.EntityFrameworkCore.DbContextOptions`1, Microsoft.EntityFrameworkCore";

    // The service type to resolve for the application's DbContextOptions registration: the base
    // DbContext when a host registered it explicitly, otherwise the concrete context the options name.
    // Held on this singleton and keyed by the non-generic DbContextOptions type — stable for the
    // application — so the registration is inspected once and every later dispatch, from any DI scope,
    // skips straight to a container lookup. A null value is a context the container cannot name at all;
    // ConcurrentDictionary stores null reference-type values, so no wrapper box is needed to cache it.
    private readonly ConcurrentDictionary<Type, Type?> _targetTypeByOptionsType = new();

    private int _targetTypeResolutions;

    // How many times the ContextType reflection has actually run. Held so a test can prove the cache
    // keeps it at one per application even when each DI scope hands out its own DbContextOptions
    // instance under a plain AddDbContext<T>() registration.
    internal int TargetTypeResolutions => Volatile.Read(ref _targetTypeResolutions);

    /// <summary>
    /// Resolves the concrete <c>DbContext</c> instance the transaction should run on, or
    /// <see langword="null"/> when EF Core is not loaded or no context is registered.
    /// </summary>
    /// <param name="serviceProvider">The scope's provider, used to resolve the context instance.</param>
    /// <returns>The resolved <c>DbContext</c>, or <see langword="null"/> when none is available.</returns>
    // Resolves the EF Core marker types by name and delegates to the covered overload. Only the
    // not-loaded guard is unreachable from a test process that references EF Core, so just that branch
    // is isolated into the excluded helper below; the marker lookups and delegation stay counted.
    public object? Resolve(IServiceProvider serviceProvider)
    {
        var dbContextType = Type.GetType(DbContextTypeName);
        var optionsType = Type.GetType(DbContextOptionsTypeName);
        return dbContextType is null || optionsType is null
            ? NotLoaded()
            : Resolve(serviceProvider, dbContextType, optionsType);
    }

    // The EF-not-loaded outcome. A test process referencing EF Core cannot reach it, so it is the only
    // part of Resolve(IServiceProvider) excluded from coverage.
    [ExcludeFromCodeCoverage]
    private static object? NotLoaded() => null;

    /// <summary>
    /// Resolves the concrete <c>DbContext</c> instance over marker types the caller has already
    /// resolved: the container's base <c>DbContext</c> when a host registered it explicitly, otherwise
    /// the concrete context named by the registered <c>DbContextOptions</c>. The target <em>type</em> is
    /// cached on this singleton so the reflection runs once; the instance is resolved from
    /// <paramref name="serviceProvider"/> — the request scope — on every call.
    /// </summary>
    internal object? Resolve(IServiceProvider serviceProvider, Type dbContextType, Type optionsType)
    {
        var options = serviceProvider.GetService(optionsType);
        if (options is null)
            return serviceProvider.GetService(dbContextType);

        var target = _targetTypeByOptionsType.GetOrAdd(
            optionsType, _ => ResolveTargetType(serviceProvider, dbContextType, options));

        return target is null ? null : serviceProvider.GetService(target);
    }

    /// <summary>
    /// The concrete context types the container has a <c>DbContextOptions&lt;T&gt;</c> registration for
    /// that the fallback could silently pick between — more than one, with no explicit base
    /// <c>DbContext</c> registration to disambiguate. Empty when the choice is unambiguous (a single
    /// context, or an explicit base) or EF Core is not loaded.
    /// </summary>
    /// <param name="services">The live service collection to inspect.</param>
    /// <returns>The competing context types when ambiguous; an empty list otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    // Resolves the EF Core marker types by name and delegates to the covered overload. Only the
    // not-loaded guard is unreachable from a test process that references EF Core, so just that branch
    // is isolated into the excluded helper below; the marker lookups and delegation stay counted.
    public IReadOnlyList<Type> ListAmbiguousContexts(IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var dbContextType = Type.GetType(DbContextTypeName);
        var optionsOpenType = Type.GetType(DbContextOptionsOpenTypeName);
        return dbContextType is null || optionsOpenType is null
            ? NotLoadedContexts()
            : ListAmbiguousContexts(services, dbContextType, optionsOpenType);
    }

    // The EF-not-loaded outcome. A test process referencing EF Core cannot reach it, so it is the only
    // part of ListAmbiguousContexts(IServiceCollection) excluded from coverage.
    [ExcludeFromCodeCoverage]
    private static IReadOnlyList<Type> NotLoadedContexts() => [];

    /// <summary>
    /// The ambiguity rule, over marker types the caller has already resolved: the distinct concrete
    /// contexts named by the registered <c>DbContextOptions&lt;T&gt;</c> descriptors, returned only
    /// when there is more than one and no explicit base <c>DbContext</c> registration disambiguates
    /// them.
    /// </summary>
    internal static IReadOnlyList<Type> ListAmbiguousContexts(
        IServiceCollection services, Type dbContextType, Type optionsOpenType)
    {
        var hasExplicitBase = services.Any(descriptor => descriptor.ServiceType == dbContextType);
        if (hasExplicitBase)
            return [];

        var contextTypes = services
            .Where(descriptor =>
                descriptor.ServiceType.IsGenericType &&
                descriptor.ServiceType.GetGenericTypeDefinition() == optionsOpenType)
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .Distinct()
            .ToList();

        return contextTypes.Count > 1 ? contextTypes : [];
    }

    // The concrete service type to resolve on every later dispatch: the base DbContext when a host
    // registered it explicitly — resolving it here proves the registration — otherwise the concrete
    // context named by the options. Runs only when EF Core is loaded and is covered through HandleAsync
    // and the plain-AddDbContext tests; the cache above keeps it to one run per application.
    private Type? ResolveTargetType(IServiceProvider serviceProvider, Type dbContextType, object options)
    {
        Interlocked.Increment(ref _targetTypeResolutions);

        if (serviceProvider.GetService(dbContextType) is not null)
            return dbContextType;

        return options.GetType().GetProperty("ContextType")?.GetValue(options) as Type;
    }
}
