using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Bsft.Mediator;

/// <summary>
/// Fluent configuration class for registering mediator handlers and pipeline behaviors.
/// </summary>
public sealed class MediatorConfiguration
{
    /// <summary>
    /// The diagnostic for forwarding that is enabled without a bridge to carry it. Shared by the
    /// startup validator and the dispatcher so the two cannot drift apart.
    /// </summary>
    internal const string MissingForwarderMessage =
        """
        Notification forwarding is enabled but no INotificationForwarder is registered.
        Register the bridge (for example AddBsftMediatorEventAggregator()) or remove
        ForwardNotifications() from the mediator configuration.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorConfiguration"/> class over the
    /// canonical default behaviors.
    /// </summary>
    public MediatorConfiguration()
        : this(new DefaultBehaviorCatalog())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorConfiguration"/> class over the given
    /// default behaviors. Used by the tests that need a slot the canonical catalog does not hold.
    /// </summary>
    /// <param name="catalog">The default behaviors and their order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> is null.</exception>
    internal MediatorConfiguration(DefaultBehaviorCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    internal DefaultBehaviorCatalog Catalog { get; }
    internal List<Assembly> Assemblies { get; } = [];
    internal List<Type> BehaviorTypes { get; } = [];
    internal List<string> SuppressedDefaults { get; } = [];
    internal bool ShouldForwardNotifications { get; private set; }
    internal bool ShouldTrace { get; private set; }
    internal bool ShouldUseDefaults { get; private set; }

    /// <summary>
    /// Scans the specified assembly for command handlers, query handlers, and notification handlers
    /// and registers them with the service collection.
    /// </summary>
    /// <param name="assembly">The assembly to scan for handler implementations.</param>
    /// <returns>The current configuration instance for method chaining.</returns>
    public MediatorConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        if (assembly is null)
            throw new ArgumentNullException(nameof(assembly));

        Assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Adds a pipeline behavior to the mediator pipeline in the order it is registered.
    /// </summary>
    /// <remarks>
    /// The behavior must be an open generic type definition such as <c>typeof(LoggingBehavior&lt;,&gt;)</c>.
    /// The mediator resolves behaviors by the <em>static</em> request type it dispatches under —
    /// <see cref="ICommand{TResult}"/> or <see cref="IQuery{TResult}"/>, never the concrete request
    /// class — so a behavior closed over a concrete request type can never enter the pipeline and is
    /// rejected here rather than registered into silence. Restrict such a behavior to the requests it
    /// cares about inside <see cref="IPipelineBehavior{TRequest, TResponse}.HandleAsync"/> instead.
    /// </remarks>
    /// <param name="behaviorType">The open generic pipeline behavior type definition.</param>
    /// <returns>The current configuration instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="behaviorType"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="behaviorType"/> is not an open
    /// generic type definition with two type parameters implementing
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/>.</exception>
    public MediatorConfiguration AddBehavior(Type behaviorType)
    {
        if (behaviorType is null)
            throw new ArgumentNullException(nameof(behaviorType));

        ValidateBehaviorType(behaviorType);

        BehaviorTypes.Add(behaviorType);
        return this;
    }

    /// <summary>
    /// Enables forwarding of published notifications to the registered
    /// <see cref="INotificationForwarder"/>, which is what carries a mediator notification to the
    /// in-process event aggregator and the sagas subscribed to it.
    /// </summary>
    /// <remarks>
    /// Forwarding needs an <see cref="INotificationForwarder"/> in the container — register the
    /// bridge package <c>Bsft.Mediator.EventAggregator</c> via <c>AddBsftMediatorEventAggregator()</c>
    /// or supply an own implementation. A missing forwarder is reported by the mediator startup
    /// validator rather than silently dropping notifications.
    /// </remarks>
    /// <returns>The current configuration instance for method chaining.</returns>
    public MediatorConfiguration ForwardNotifications()
    {
        ShouldForwardNotifications = true;
        return this;
    }

    /// <summary>
    /// Enables tracing and metrics for command and query handling. The
    /// <see cref="TracingBehavior{TRequest, TResponse}"/> is registered as the outermost pipeline
    /// behavior so its span wraps every other behavior and the handler.
    /// </summary>
    /// <returns>The current configuration instance for method chaining.</returns>
    public MediatorConfiguration EnableTracing()
    {
        ShouldTrace = true;
        return this;
    }

    /// <summary>
    /// Registers the canonical cross-cutting pipeline: tracing, authorization, validation, audit,
    /// retry, transaction and caching, outermost first, from whichever behavior packages the
    /// application references. A package that is not referenced contributes nothing and the host
    /// writes no conditional for it.
    /// </summary>
    /// <remarks>
    /// The order is the framework's, not the host's, because getting it wrong is a security bug:
    /// caching ahead of authorization serves a cached result to a caller who was never allowed to
    /// see it. Behaviors the host adds through <see cref="AddBehavior"/> or directly against
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/> keep the position they were registered
    /// in, around the default ones.
    /// <para>
    /// Tracing is part of the chain, so <see cref="EnableTracing"/> is implied and calling both
    /// still yields exactly one tracing behavior. The escape hatches are
    /// <see cref="SuppressDefaultBehavior"/> for a single behavior and omitting this call for the
    /// chain as a whole — without it, registration order is the pipeline order, as before.
    /// </para>
    /// <para>
    /// A behavior whose package is referenced but never registered — <c>Bsft.Caching</c> without
    /// <c>AddBsftCaching</c> — is reported by the mediator startup validator naming the missing
    /// call, instead of failing on the first dispatch that needs it.
    /// </para>
    /// </remarks>
    /// <returns>The current configuration instance for method chaining.</returns>
    public MediatorConfiguration UseDefaults()
    {
        ShouldUseDefaults = true;
        return this;
    }

    /// <summary>
    /// Removes one behavior from the default chain, leaving the rest in their canonical order.
    /// </summary>
    /// <remarks>
    /// A name matching no default behavior is rejected here rather than ignored: the mistake a
    /// silent no-op hides is a host that believes it disabled a behavior and one that believes a
    /// security behavior is still in place, and neither is worth the convenience.
    /// </remarks>
    /// <param name="name">The behavior name, from <see cref="MediatorDefaults"/>.</param>
    /// <returns>The current configuration instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> matches no default behavior.</exception>
    public MediatorConfiguration SuppressDefaultBehavior(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        if (!Catalog.IsKnown(name))
            throw new ArgumentException(
                $"'{name}' is not a default pipeline behavior. Known behaviors: {Catalog.DescribeKnownNames()}.",
                nameof(name));

        SuppressedDefaults.Add(name);
        return this;
    }

    internal void RegisterHandlers(IServiceCollection services)
    {
        var handlerInterfaceTypes = new[]
        {
            typeof(ICommandHandler<,>),
            typeof(ICommandHandler<>),
            typeof(IQueryHandler<,>),
            typeof(INotificationHandler<>)
        };

        foreach (var assembly in Assemblies)
        {
            var types = AssemblyTypes.Loadable(assembly);
            RegisterHandlerTypes(services, types, handlerInterfaceTypes);
        }
    }

    private static void RegisterHandlerTypes(
        IServiceCollection services, IEnumerable<Type> types, Type[] handlerInterfaceTypes)
    {
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract)
                continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType)
                    continue;

                var genericDef = iface.GetGenericTypeDefinition();
                if (handlerInterfaceTypes.Any(hi => hi == genericDef))
                    services.AddTransient(iface, type);
            }
        }
    }

    internal void RegisterBehaviors(IServiceCollection services)
    {
        // The default chain contains tracing, so asking for the defaults enables it — including for
        // notifications, which bypass the pipeline and are instrumented by the dispatcher itself.
        if (ShouldUseDefaults && !SuppressedDefaults.Contains(MediatorDefaults.Tracing, StringComparer.OrdinalIgnoreCase))
            ShouldTrace = true;

        // Tracing is registered first so it resolves as the outermost pipeline behavior, wrapping
        // every user-registered behavior and the handler regardless of registration order.
        if (ShouldTrace)
            AddBehaviorOnce(services, typeof(TracingBehavior<,>));

        if (ShouldUseDefaults)
            RegisterDefaultBehaviors(services);

        foreach (var behaviorType in BehaviorTypes)
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), behaviorType);
        }
    }

    /// <summary>
    /// Registers every default behavior whose package is referenced, and the orderer that folds them
    /// in canonical order. Registration is de-duplicated because the behavior packages register
    /// their own behavior too, and the order is applied per dispatch because those calls may run
    /// after this one.
    /// </summary>
    private void RegisterDefaultBehaviors(IServiceCollection services)
    {
        foreach (var slot in Catalog.Available(SuppressedDefaults))
        {
            AddBehaviorOnce(services, slot.Value);
        }

        services.AddSingleton(Catalog);
        services.AddSingleton<IPipelineBehaviorOrderer, CanonicalPipelineBehaviorOrderer>();
    }

    /// <summary>
    /// Registers a behavior unless the same implementation is already registered, so the defaults
    /// and a behavior package's own <c>AddBsft*</c> call cannot add it twice.
    /// </summary>
    private static void AddBehaviorOnce(IServiceCollection services, Type behaviorType) =>
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), behaviorType));

    private static void ValidateBehaviorType(Type behaviorType)
    {
        if (!behaviorType.IsGenericTypeDefinition)
            throw new ArgumentException(
                $"""
                 Pipeline behavior '{behaviorType.Name}' must be an open generic type definition,
                 such as typeof(MyBehavior<,>). The mediator resolves behaviors by the static request
                 type (ICommand<TResult> / IQuery<TResult>), so a behavior closed over a concrete
                 request type would never be invoked. Filter on the request type inside HandleAsync.
                 """,
                nameof(behaviorType));

        if (behaviorType.GetGenericArguments().Length != 2)
            throw new ArgumentException(
                $"""
                 Pipeline behavior '{behaviorType.Name}' must declare exactly two type parameters,
                 matching IPipelineBehavior<TRequest, TResponse>.
                 """,
                nameof(behaviorType));

        var implementsBehavior = behaviorType.GetInterfaces().Any(iface =>
            iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        if (!implementsBehavior)
            throw new ArgumentException(
                $"Pipeline behavior '{behaviorType.Name}' must implement IPipelineBehavior<TRequest, TResponse>.",
                nameof(behaviorType));
    }
}
