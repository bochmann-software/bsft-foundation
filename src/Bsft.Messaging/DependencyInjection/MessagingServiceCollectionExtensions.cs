using Bsft.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Bsft.Instrumentation.DependencyInjection;
using Bsft.Messaging.Consuming;
using Bsft.Messaging.Diagnostics;
using Bsft.Messaging.Inbox;
using Bsft.Messaging.Loopback;
using Bsft.Messaging.Outbox;
using Bsft.Messaging.Publishing;

namespace Bsft.Messaging.DependencyInjection;

/// <summary>
/// Extension methods for registering the messaging core: integration event staging, the type registry
/// and the in-memory message bus.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the messaging core: the type registry, the payload serializer, the in-memory bus, the
    /// scoped event collector and the ambient correlation seam. Everything is registered with
    /// <c>TryAdd</c>, so a broker adapter registered beforehand keeps its place and only the parts it
    /// does not replace are filled in. Persisting the staged events is the job of a storage package
    /// such as <c>Bsft.Messaging.EFCore</c>; without one, staged events are never delivered. Add
    /// <c>Bsft.Messaging.Correlation</c> to have the correlation id of the staging request travel with
    /// the message.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action declaring the integration event types.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// The options are validated by <c>ValidateOnStart()</c>, so a value that cannot be used fails the
    /// host rather than this call, and fails it with every complaint listed at once.
    /// </remarks>
    public static IServiceCollection AddBsftMessaging(
        this IServiceCollection services, Action<BsftMessagingOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers the messaging core with its options bound from configuration section
    /// <see cref="BsftMessagingOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">
    /// An optional action applied after the section has been bound. The event vocabulary is declared
    /// here rather than in configuration, because it names CLR types.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <remarks>
    /// The section is bound first and the delegate runs after it, so code wins over configuration for
    /// anything both of them set. Everything the other overload documents applies here as well.
    /// </remarks>
    public static IServiceCollection AddBsftMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftMessagingOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        return AddCore(services, options =>
        {
            configuration.GetSection(BsftMessagingOptions.SectionName).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers everything both entry-point overloads register.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddCore(
        IServiceCollection services, Action<BsftMessagingOptions>? configure)
    {
        AddMessagingOptions(services, configure);

        services.TryAddSingleton(TimeProvider.System);
        AddRetrySchedule(services);
        AddOutboxDrainer(services);
        services.TryAddSingleton<IIntegrationEventTypeRegistry, IntegrationEventTypeRegistry>();
        services.TryAddSingleton<IIntegrationEventSerializer, JsonIntegrationEventSerializer>();
        services.TryAddSingleton<IIntegrationEventHandlerInvoker, IntegrationEventHandlerInvoker>();
        services.TryAddSingleton<IMessageBus, InMemoryMessageBus>();

        // The ambient correlation id flows with the asynchronous control flow, so the component that
        // restores it while a message is handled and the ones that read it while staging the next
        // event have to share one instance.
        services.TryAddSingleton<IMessageCorrelationScope, MessageCorrelationScope>();

        // Scoped, not singleton, although this default needs nothing from a scope: the bridge in
        // Bsft.Messaging.Correlation replaces it with one that resolves the scoped ICorrelationContext,
        // and a default of a wider lifetime would leave the two registrations disagreeing about how
        // long an accessor may live.
        services.TryAddScoped<IMessageCorrelationAccessor, AmbientMessageCorrelationAccessor>();

        // One instance per scope serves both sides of the staging contract, so what application code
        // stages is exactly what the persistence layer drains within the same unit of work.
        services.TryAddScoped<IntegrationEventCollector>();
        services.TryAddScoped<IIntegrationEventCollector>(sp => sp.GetRequiredService<IntegrationEventCollector>());
        services.TryAddScoped<IIntegrationEventBuffer>(sp => sp.GetRequiredService<IntegrationEventCollector>());

        return services;
    }

    /// <summary>
    /// Registers the retry schedule both halves of the pipeline compute their waits with.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <remarks>
    /// It is registered by the producing and the consuming entry point alike, because either may be
    /// wired up without the other and both fail a message sooner or later. <see cref="Random.Shared"/>
    /// supplies the jitter — the schedule takes its randomness as a dependency so a test can assert the
    /// whole schedule, and nothing here is a security decision.
    /// </remarks>
    private static void AddRetrySchedule(IServiceCollection services)
    {
        services.TryAddSingleton<IOutboxRetrySchedule>(_ => new OutboxRetrySchedule(Random.Shared));
        services.TryAddSingleton<IMessageRetrySchedule>(sp =>
            new MessageRetrySchedule(sp.GetRequiredService<IOutboxRetrySchedule>()));
    }

    /// <summary>
    /// Registers the drain pass this package's outbox runs on, composed from the shared kernel and the
    /// three seams that make it about integration events.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <remarks>
    /// <para>
    /// This is the composition root of the outbox, and it is deliberately the only place the three
    /// adapters are built: the drainer is the kernel's, the store is whichever
    /// <see cref="IOutboxStore"/> the host registered, and the schedule is reached through
    /// <see cref="IMessageRetrySchedule"/> so a host that replaces that one still decides how long a
    /// failed message waits.
    /// </para>
    /// <para>
    /// The store is resolved when the drainer is first asked for rather than here, so
    /// <c>AddBsftMessagingEFCore</c> may register the durable one after this call.
    /// </para>
    /// </remarks>
    private static void AddOutboxDrainer(IServiceCollection services)
    {
        services.TryAddSingleton<IOutboxDrainer<IntegrationEventMessage>>(sp =>
            new OutboxDrainer<IntegrationEventMessage>(
                new MessagingOutboxStore(sp.GetRequiredService<IOutboxStore>()),
                new IntegrationEventPublication(sp.GetRequiredService<IMessageBus>()),
                new MessagingOutboxRetrySchedule(sp.GetRequiredService<IMessageRetrySchedule>()),
                DrainBounds(sp.GetRequiredService<IOptions<BsftMessagingOptions>>().Value.Outbox),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<OutboxDispatcher>()));
    }

    /// <summary>
    /// Restates this package's outbox group as what the drain kernel is bounded by. The two carry the
    /// same six settings under the same names, and the group stays where a host configures it —
    /// <c>Bsft:Messaging:Outbox</c> — rather than moving to the kernel's own section.
    /// </summary>
    /// <param name="options">The outbox group of the messaging options.</param>
    /// <returns>The same bounds, as the kernel spells them.</returns>
    private static BsftOutboxOptions DrainBounds(OutboxOptions options) => new()
    {
        PollInterval = options.PollInterval,
        BatchSize = options.BatchSize,
        LeaseDuration = options.LeaseDuration,
        MaxDeliveryAttempts = options.MaxDeliveryAttempts,
        RetryBackoff = options.RetryBackoff,
        MaxRetryDelay = options.MaxRetryDelay,
    };

    /// <summary>
    /// Registers <see cref="BsftMessagingOptions"/> with its validator and start-up validation.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action shaping the options, applied in registration order.</param>
    /// <remarks>
    /// Every entry point of the family funnels through here, so the options are registered once
    /// however the host reached them, and the delegates of several calls are all applied rather than
    /// the last one silently discarding the others.
    /// </remarks>
    private static void AddMessagingOptions(
        IServiceCollection services, Action<BsftMessagingOptions>? configure)
    {
        services.AddOptions<BsftMessagingOptions>()
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<BsftMessagingOptions>, BsftMessagingOptionsValidator>());
    }

    /// <summary>
    /// Registers the inbox: the record of which events this consumer has already processed, and the
    /// guard that keeps a redelivery from reaching a handler. Defaults to the in-memory store, which
    /// forgets everything when the process stops; add <c>AddBsftInboxEFCore</c> from
    /// <c>Bsft.Messaging.EFCore</c> beforehand to have the record survive a restart and to have the
    /// mark commit in the same transaction as the handler's own writes.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure the consumer options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Call <see cref="AddBsftMessaging(IServiceCollection, Action{BsftMessagingOptions})"/> as well:
    /// the inbox-guarded delivery runs the handlers through the invoker and the ambient correlation
    /// scope the messaging core registers, and without it the container cannot construct it.
    /// </para>
    /// <para>
    /// It configures <see cref="BsftMessagingOptions.Consumer"/>, so it is also where
    /// <see cref="ConsumerOptions.ConsumerName"/> is set when the inbox is wired up without the host.
    /// Calling it twice applies both delegates in order rather than one of them winning at random,
    /// which is why <see cref="AddBsftConsumerHost"/> calls this rather than repeating it.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftInbox(
        this IServiceCollection services, Action<ConsumerOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        AddMessagingOptions(services, options => configure?.Invoke(options.Consumer));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryInboxLedger>();

        // Scoped, and resolved from the delivery's own scope rather than injected into the delivery:
        // that is what lets the EF Core store reach the very DbContext the handlers write through, and
        // so what makes the mark and the handler's work one transaction.
        services.TryAddScoped<IInboxStore, InMemoryInboxStore>();

        services.TryAddSingleton<IInboxGuardedDelivery, InboxGuardedDelivery>();

        return services;
    }

    /// <summary>
    /// Wires the consuming pipeline: the inbox, the message decoder, the consumer and the
    /// <see cref="IntegrationEventConsumerHost"/> background service. It needs an
    /// <see cref="IMessageSource"/> to consume from — a broker adapter registers one, and
    /// <see cref="AddBsftMessagingLoopback"/> registers the in-process one.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">
    /// An action to configure the consumer options. <see cref="ConsumerOptions.ConsumerName"/> has no
    /// default, and the host refuses to start without it.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Call <see cref="AddBsftMessaging(IServiceCollection, Action{BsftMessagingOptions})"/> as well,
    /// and declare every consumed event type there: the decoder resolves an event name through that
    /// registry, and an event this host is not told about is parked rather than handled.
    /// </remarks>
    public static IServiceCollection AddBsftConsumerHost(
        this IServiceCollection services, Action<ConsumerOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddBsftInbox(configure);

        AddRetrySchedule(services);

        services.AddBsftInstrumentationSource(ConsumerTelemetry.SourceName);
        services.TryAddSingleton<IConsumerTelemetry, ConsumerTelemetry>();

        services.TryAddSingleton<IIncomingMessageDecoder, IncomingMessageDecoder>();
        services.TryAddSingleton<IIntegrationEventConsumer, IntegrationEventConsumer>();

        services.AddHostedService<IntegrationEventConsumerHost>();

        return services;
    }

    /// <summary>
    /// Registers the in-process loopback transport as both the message bus and the message source, so
    /// a single-process deployment runs the same consuming pipeline it would against a broker.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Call it before <see cref="AddBsftMessaging(IServiceCollection, Action{BsftMessagingOptions})"/>,
    /// which registers <see cref="InMemoryMessageBus"/> with <c>TryAdd</c> and would otherwise take the
    /// bus registration first — the same ordering rule a broker adapter follows. It replaces the bus,
    /// so a publish then returns once the message is queued instead of once the handlers have run: a
    /// handler failure no longer fails the outbox publish, and is retried and parked by the consumer
    /// host instead.
    /// </remarks>
    public static IServiceCollection AddBsftMessagingLoopback(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        AddMessagingOptions(services, configure: null);

        services.TryAddSingleton(TimeProvider.System);

        // The parked list's bound is read through IOptions, so the ordering rule this call used to
        // carry — register before the consuming side or lose whatever it configured — is gone: the
        // value is resolved once the container is built and every Configure delegate has run.
        services.TryAddSingleton<LoopbackMessageTransport>();

        services.TryAddSingleton<IMessageBus>(sp => sp.GetRequiredService<LoopbackMessageTransport>());
        services.TryAddSingleton<IMessageSource>(sp => sp.GetRequiredService<LoopbackMessageTransport>());

        return services;
    }

    /// <summary>
    /// Registers a handler for an integration event. Several handlers may be registered for the same
    /// event; each is invoked once per delivery.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type the handler consumes.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftIntegrationEventHandler<TEvent, THandler>(
        this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler<TEvent>, THandler>());

        return services;
    }
}
