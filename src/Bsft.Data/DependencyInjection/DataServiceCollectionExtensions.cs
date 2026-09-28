using Bsft.Core.Persistence;
using Bsft.Data.Search;
using Bsft.Data.Filtering;
using Bsft.Data.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Bsft.Data.Diagnostics;
using Bsft.Instrumentation.DependencyInjection;

namespace Bsft.Data.DependencyInjection;

/// <summary>
/// Extension methods registering the two halves a host wires separately: the query-filtering and
/// search infrastructure, and multi-tenancy.
/// </summary>
/// <remarks>
/// They are two methods rather than one because a host legitimately wants either alone — a
/// single-tenant application still filters and searches, and a multi-tenant one that exposes no
/// list endpoint still has to scope its writes.
/// </remarks>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the query filtering infrastructure and scans assemblies for
    /// <see cref="QueryConfigurationBase{TEntity, TDto}"/> implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure assembly scanning.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// This half has no options type of its own: what it needs is the set of assemblies to scan,
    /// which a configuration provider cannot supply. See <see cref="BsftDataOptions"/> for the
    /// settings a deployment does state.
    /// </remarks>
    public static IServiceCollection AddBsftDataQueryFiltering(
        this IServiceCollection services,
        Action<QueryFilteringConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var config = new QueryFilteringConfiguration();
        configure(config);

        // Telemetry emits a no-op until an observability backend attaches a listener/exporter; the
        // "Bsft.Data" source is advertised for discovery via the vendor-neutral instrumentation layer.
        services.AddBsftInstrumentationSource(DataTelemetry.SourceName);
        services.TryAddSingleton<IDataTelemetry, DataTelemetry>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<Bsft.Core.IStartupValidator, SearchStartupValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<Bsft.Core.IStartupValidator, QueryConfigurationStartupValidator>());

        foreach (var assembly in config.Assemblies)
        {
            var configTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && IsQueryConfiguration(t));

            foreach (var configType in configTypes)
                RegisterQueryConfiguration(services, configType);
        }

        return services;
    }

    /// <summary>
    /// Registers the framework's unit of work over the application's own context, under both
    /// <see cref="IUnitOfWork"/> and <see cref="ITransactionalUnitOfWork"/>.
    /// </summary>
    /// <typeparam name="TContext">The application's context, which the commit and the transaction run on.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This is the registration the packages that stage rather than write are asking for: a
    /// notification digest sweep, the API key expiry reminders and any other seam typed against
    /// <see cref="IUnitOfWork"/> find their commit here instead of each host writing the same
    /// four-line adapter. Scoped, because the context it commits is.
    /// </para>
    /// <para>
    /// Registered with <c>TryAdd</c> throughout, so a host that has its own unit of work — one that
    /// spans two stores, or commits something this framework never sees — registers it first and keeps
    /// it. The context itself is not registered here: it is the host's, added with
    /// <c>AddDbContext</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftDataUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<DbContextUnitOfWork<TContext>>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbContextUnitOfWork<TContext>>());
        services.TryAddScoped<ITransactionalUnitOfWork>(
            sp => sp.GetRequiredService<DbContextUnitOfWork<TContext>>());

        return services;
    }

    /// <summary>
    /// Registers the multi-tenancy infrastructure including tenant context, resolver, and save
    /// changes interceptor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action selecting the tenant resolution strategy.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IServiceCollection AddBsftDataMultiTenancy(
        this IServiceCollection services,
        Action<MultiTenancyConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return AddMultiTenancy(services, configuration: null, configure);
    }

    /// <summary>
    /// Registers the multi-tenancy infrastructure, with the header name and claim type bound from
    /// configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Data</c> section.</param>
    /// <param name="configure">An action selecting the tenant resolution strategy.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    /// <remarks>
    /// A name stated in code — <c>ResolveFromHeader("X-Account")</c> — is applied after the section
    /// is bound and therefore wins over it. That is the order a reader expects: the section carries
    /// the deployment's default, the call carries the application's decision.
    /// </remarks>
    public static IServiceCollection AddBsftDataMultiTenancy(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MultiTenancyConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);

        return AddMultiTenancy(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers, whichever overload was called.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">An action selecting the tenant resolution strategy.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    /// <remarks>
    /// The options are validated inside this call as well as through the registered
    /// <see cref="IValidateOptions{TOptions}"/>, so a header name no request can carry is a failure of
    /// the registration rather than a tenant that silently never resolves.
    /// </remarks>
    private static IServiceCollection AddMultiTenancy(
        IServiceCollection services,
        IConfiguration? configuration,
        Action<MultiTenancyConfiguration> configure)
    {
        void Apply(BsftDataOptions target) => configuration?.GetSection(BsftDataOptions.SectionName).Bind(target);

        var config = new MultiTenancyConfiguration();
        configure(config);

        var options = new BsftDataOptions();

        Apply(options);
        config.ApplyOptions(options);
        options.Validate();

        services.AddOptions<BsftDataOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftDataOptions>, BsftDataOptionsValidator>());

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantSaveChangesInterceptor>();

        // The validator reads the service collection itself, because pooling is only ever visible as a
        // registration. Holding the collection lets it run after every AddDbContextPool call, whichever
        // order the host wrote them in.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator>(
                new PooledTenantContextStartupValidator(services)));

        // The write half of the isolation is the one a host can leave out silently: the interceptor is
        // registered here but only takes effect once it is on the context's options, and a context
        // without it writes unstamped rows that nothing complains about. The validator needs both the
        // registrations, to find the tenant-scoped contexts, and the provider, to read the options each
        // of them was built with — so the collection is registered as a service of its own. That is
        // what lets the validator be added by implementation type, which is what TryAddEnumerable needs
        // in order to keep a second AddBsftDataMultiTenancy from adding a second one.
        services.TryAddSingleton<IServiceCollection>(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator, TenantInterceptorStartupValidator>());

        // Caching is the other seam whose tenant-blindness is invisible at runtime: a host that
        // resolves tenants and keeps the global cache key scope shares cache entries across tenant
        // boundaries. The validator holds the collection for the same reason as the one above — which
        // provider backs the seam is a registration — so AddBsftCaching may come before or after this.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator>(
                new TenantCacheScopeStartupValidator(services)));

        config.Apply(services);

        return services;
    }

    /// <summary>
    /// Registers one query configuration: the configuration itself, its executor, the compiled
    /// search configuration behind both validation seams, and the search executor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationType">The discovered <see cref="QueryConfigurationBase{TEntity, TDto}"/> implementation.</param>
    private static void RegisterQueryConfiguration(IServiceCollection services, Type configurationType)
    {
        var baseType = GetQueryConfigurationBase(configurationType)!;

        var entityType = baseType.GetGenericArguments()[0];
        var dtoType = baseType.GetGenericArguments()[1];

        services.AddSingleton(baseType, configurationType);

        var executorInterface = typeof(IQueryExecutor<,>).MakeGenericType(entityType, dtoType);
        var executorImpl = typeof(QueryExecutor<,>).MakeGenericType(entityType, dtoType);
        services.AddSingleton(executorInterface, executorImpl);

        // The compiled configuration is registered separately from the executor so that startup
        // validation can read the declared groups even when no search provider is registered — which
        // is exactly the misconfiguration it has to report.
        var compiledType = typeof(CompiledSearchConfiguration<,>).MakeGenericType(entityType, dtoType);
        services.AddSingleton(compiledType);
        services.AddSingleton(sp => (ISearchGroupSource)sp.GetRequiredService(compiledType));
        services.AddSingleton(sp => (IQueryConfigurationSource)sp.GetRequiredService(compiledType));

        // Scoped, not singleton: the executor depends on the request-scoped ISearchScopeProvider, and
        // that provider reads request state — the tenant context, the signed-in principal. Resolved
        // from anywhere but the caller's own scope it would see a blank request and scope the search
        // to nothing.
        var searchInterface = typeof(ISearchExecutor<,>).MakeGenericType(entityType, dtoType);
        var searchImpl = typeof(SearchExecutor<,>).MakeGenericType(entityType, dtoType);
        services.AddScoped(searchInterface, searchImpl);
    }

    /// <summary>Whether a discovered type derives from the query configuration base.</summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsQueryConfiguration(Type type) => GetQueryConfigurationBase(type) is not null;

    /// <summary>Walks a type's base chain to the closed query configuration base, if it has one.</summary>
    /// <param name="type">The candidate type.</param>
    /// <returns>The closed generic base type, or <see langword="null"/>.</returns>
    private static Type? GetQueryConfigurationBase(Type type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(QueryConfigurationBase<,>))
                return current;

            current = current.BaseType;
        }

        return null;
    }
}
