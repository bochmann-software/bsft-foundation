using Bsft.Core.Persistence;
using Bsft.Data.Search;
using Bsft.Data.Filtering;
using Bsft.Data.Modeling;
using Bsft.Data.MultiTenancy;
using Bsft.Data.Transactions;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        services.AddBsftDataModelWarmup();
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

        services.AddBsftDataModelWarmup();
        services.TryAddScoped<DbContextUnitOfWork<TContext>>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbContextUnitOfWork<TContext>>());
        services.TryAddScoped<ITransactionalUnitOfWork>(
            sp => sp.GetRequiredService<DbContextUnitOfWork<TContext>>());

        return services;
    }

    /// <summary>
    /// Registers one database transaction per scope that spans every <see cref="DbContext"/>
    /// configured with <see cref="DataDbContextOptionsExtensions.UseBsftSharedTransaction"/>,
    /// on one connection those contexts share.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionFactory">
    /// Creates the scope's connection, unopened, for example
    /// <c>_ =&gt; new NpgsqlConnection(connectionString)</c>. It is called once per scope that needs it,
    /// and the connection is disposed with the scope.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// <para>
    /// This is the opt-in for a modular host whose modules each own a context over one database: the
    /// outermost unit opens the transaction, every unit started inside it — a nested
    /// <c>[Transactional]</c> command, a package typed against <see cref="ITransactionalUnitOfWork"/> —
    /// joins it, and it commits once. The registration is <see cref="IAmbientTransaction"/>, which the
    /// mediator's transaction and retry behaviors pick up, plus <see cref="ITransactionalUnitOfWork"/> and
    /// <see cref="IUnitOfWork"/> over the same instance. A host that does not call this keeps the
    /// single-context behavior unchanged.
    /// </para>
    /// <para>
    /// Registered with <c>TryAdd</c>; a startup validator fails when
    /// <see cref="AddBsftDataUnitOfWork{TContext}"/> is registered as well, because that unit would open
    /// a second transaction on the shared connection. The isolation level is
    /// <see cref="BsftDataOptions.SharedTransaction"/>; a provider package registers its
    /// <see cref="ITransactionConflictClassifier"/> beside this. A host that has to see the steps the
    /// shared transaction runs on the connection outside EF Core registers a scoped
    /// <see cref="ISharedTransactionStepObserver"/>; without one nothing is reported.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftDataSharedTransaction(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        services.AddBsftDataModelWarmup();
        services.TryAddScoped<SharedTransactionEnlistment>();
        services.TryAddScoped<SharedContextProvider>();
        services.TryAddScoped<SharedTransactionInterceptor>();
        services.TryAddScoped<SharedTransactionHealth>();
        services.TryAddScoped(serviceProvider => new SharedTransactionStepReporter(
            serviceProvider.GetServices<ISharedTransactionStepObserver>(),
            serviceProvider.GetService<ILogger<SharedTransactionStepReporter>>()
                ?? NullLogger<SharedTransactionStepReporter>.Instance));
        services.TryAddSingleton(serviceProvider => new SharedTransactionRoot(serviceProvider));
        services.TryAddSingleton(_ => new SharedContextLifetimeListener().Subscribe());
        services.TryAddScoped(serviceProvider => new SharedTransaction(
            connectionFactory(serviceProvider),
            serviceProvider.GetRequiredService<SharedContextProvider>(),
            serviceProvider.GetRequiredService<IOptions<BsftDataOptions>>(),
            serviceProvider.GetRequiredService<SharedTransactionHealth>(),
            serviceProvider.GetRequiredService<SharedTransactionStepReporter>(),
            serviceProvider.GetService<ILogger<SharedTransaction>>() ?? NullLogger<SharedTransaction>.Instance));

        services.TryAddScoped<IAmbientTransaction>(sp => sp.GetRequiredService<SharedTransaction>());
        services.TryAddScoped<ITransactionalUnitOfWork>(sp => sp.GetRequiredService<SharedTransaction>());
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SharedTransaction>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator>(new SharedTransactionStartupValidator(services)));

        return services;
    }

    /// <summary>
    /// Registers the <see cref="ITimeOrderedIdGenerator"/> that fills the <c>Id</c> of every row of a
    /// tenant-keyed framework table, with the node id this process uses.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets <see cref="TenantKeyOptions.NodeId"/>, or <see langword="null"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The tables themselves are switched in the model, with <c>modelBuilder.UseTenantKeys()</c>
    /// (<see cref="TenantKeyModelBuilderExtensions.UseTenantKeys"/>), and this call is required with it:
    /// it says which node id the ids carry and registers the start check that refuses a pooled or
    /// factory-made tenant-keyed context. A multi-tenancy host whose model uses tenant keys without this
    /// call fails at start. Without a node id the ids come from
    /// <see cref="TimeOrderedIdGenerator.ForRandomNode"/>, whose node id is drawn at random per process and
    /// differs per clock, which is enough for a single process.
    /// </para>
    /// <para>
    /// The generator for a node id is one instance per process and clock
    /// (<see cref="TimeOrderedIdGenerator.ForNode"/>), so several containers in one process share its
    /// counter. Its time field is read from the registered <see cref="TimeProvider"/>, which this call
    /// registers as <see cref="TimeProvider.System"/> when the host has none. The node id is validated
    /// at start-up.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftDataTenantKeys(
        this IServiceCollection services, Action<TenantKeyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BsftDataOptions>()
            .Configure(options => configure?.Invoke(options.TenantKeys))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftDataOptions>, BsftDataOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITimeOrderedIdGenerator>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<BsftDataOptions>>().Value.TenantKeys.NodeId is { } nodeId
                ? TimeOrderedIdGenerator.ForNode(nodeId, serviceProvider.GetRequiredService<TimeProvider>())
                : TimeOrderedIdGenerator.ForRandomNode(serviceProvider.GetRequiredService<TimeProvider>()));
        // Registered here only, with the switch: pooled and factory-made contexts are a tenant risk
        // only under tenant keys, and a host without them must not see a new start check.
        services.TryAddSingleton<IServiceCollection>(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator, TenantKeyStartupValidator>());
        // What the multi-tenancy start check looks for: UseTenantKeys without this call is refused.
        services.TryAddSingleton<TenantKeyRegistration>();

        return services;
    }

    /// <summary>
    /// Registers the hosted service that builds the relational model of each relational context type
    /// as its first instance initializes, before the host starts serving.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// EF Core builds a model's relational model lazily, and two contexts of a new model that run
    /// their first command at the same moment can fail with "The model must be finalized and its
    /// runtime dependencies must be initialized before 'GetRelationalModel' can be used". The service
    /// builds it as each context initializes, one at a time, which is before any of them can run a
    /// command. It covers every context in the process however it is registered, and contexts on a
    /// non-relational provider are left alone.
    /// </para>
    /// <para>
    /// <see cref="AddBsftDataQueryFiltering"/>, <see cref="AddBsftDataUnitOfWork{TContext}"/> and
    /// both <c>AddBsftDataMultiTenancy</c> overloads call this; a host that uses EF Core without any
    /// of them calls it itself. Calling it again adds nothing. To keep EF Core's lazy build, set
    /// <see cref="BsftDataOptions.ShouldWarmRelationalModels"/> to <see langword="false"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftDataModelWarmup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BsftDataOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RelationalModelWarmupHostedService>());

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

        services.AddBsftDataModelWarmup();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantSaveChangesInterceptor>();

        // The scope a background service opens for one tenant's unit of work. TryAdd, so that a host whose
        // tenant scopes need more than the tenant (an identity, a database binding) registers its own first.
        services.TryAddSingleton<Bsft.Core.Tenancy.ITenantScopeFactory, TenantScopeFactory>();

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

        // The other half of the tenant-key switch: a model mapped with UseTenantKeys in a host that never
        // called AddBsftDataTenantKeys is refused. It does nothing once that call was made.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Bsft.Core.IStartupValidator, TenantKeyRegistrationStartupValidator>());

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
        services.TryAddSingleton(TimeProvider.System);
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
