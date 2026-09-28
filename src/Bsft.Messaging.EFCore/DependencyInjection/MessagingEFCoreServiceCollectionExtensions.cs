using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.Inbox;
using Bsft.Messaging.Outbox;
using Bsft.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Messaging.EFCore.DependencyInjection;

/// <summary>
/// Extension methods for registering the EF Core transactional outbox and the durable inbox.
/// </summary>
public static class MessagingEFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="OutboxInterceptor"/> and maps <see cref="IOutboxDbContext"/> to the
    /// application's <typeparamref name="TContext"/>, so staged integration events are written in the
    /// same transaction as the business change. Add the interceptor to the context with
    /// <c>DbContextOptionsBuilder.AddInterceptors</c>. Use this overload when another component owns
    /// the delivery side; <see cref="AddBsftMessagingEFCore{TContext}"/> adds the dispatcher as well.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext type implementing <see cref="IOutboxDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext, IOutboxDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddScoped<OutboxInterceptor>();
        services.TryAddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TContext>());

        // The operator's read side, registered with the write side rather than behind a call of its
        // own: it needs exactly the context that was just registered, and a parked message nobody can
        // enumerate is not a feature to opt into.
        services.TryAddScoped<IOutboxMessageReader, EFCoreOutboxMessageReader>();

        return services;
    }

    /// <summary>
    /// Registers the durable EF Core inbox: <see cref="IInboxStore"/> is served by
    /// <see cref="EFCoreInboxStore"/> against the application's <typeparamref name="TContext"/>, so the
    /// record that an event was handled is written in the same transaction as the handler's own
    /// changes. Apply <see cref="InboxMessageEntityConfiguration"/> in <c>OnModelCreating</c> and add a
    /// migration for the <c>InboxMessages</c> table.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext type implementing <see cref="IInboxDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Call it before <c>AddBsftConsumerHost</c>, which registers the in-memory store with
    /// <c>TryAdd</c> as its default and would otherwise take the <see cref="IInboxStore"/> registration
    /// first. Getting the order wrong is not loud — the consumer runs, and only forgets what it has
    /// processed whenever the process restarts.
    /// </para>
    /// <para>
    /// It also adds the <see cref="InboxSweeper"/> hosted service, which deletes records older than
    /// <see cref="ConsumerOptions.InboxRetention"/> and does nothing at all while that is unset. It is
    /// added unconditionally because the consumer options are configured by a later call and are read
    /// through <c>IOptions</c> once the container is built.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftInboxEFCore<TContext>(this IServiceCollection services)
        where TContext : DbContext, IInboxDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.TryAddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped<IInboxStore, EFCoreInboxStore>();
        services.TryAddScoped<IInboxMessageReader, EFCoreInboxMessageReader>();

        services.TryAddSingleton(TimeProvider.System);

        // The retention is read through IOptions, so the ordering rule this call used to carry — come
        // before AddBsftConsumerHost or lose whatever it configured — is gone: the value is resolved
        // once the container is built and every Configure delegate has run. Without the consuming side
        // there is no retention to have been set, and the pruner leaves at once.
        services.AddOptions<BsftMessagingOptions>().ValidateOnStart();

        services.AddHostedService<InboxSweeper>();

        return services;
    }

    /// <summary>
    /// Wires the full EF Core outbox path: the interceptor and the <see cref="IOutboxDbContext"/>
    /// mapping, the leasing <see cref="EFCoreOutboxStore"/>, the dispatcher options and the
    /// <see cref="OutboxDispatcher"/> background service. Call <c>AddBsftMessaging</c> as well, so the
    /// messaging core (event registry and message bus) is registered, and declare every published
    /// event type there.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext type implementing <see cref="IOutboxDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftMessagingOptions.Outbox"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftMessagingEFCore<TContext>(
        this IServiceCollection services, Action<OutboxOptions>? configure = null)
        where TContext : DbContext, IOutboxDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddBsftOutbox<TContext>();

        // The lease protocol itself is the shared one; what this package adds to it is the table its
        // rows live in and how one of them reads as an integration event.
        services
            .TryAddSingleton<IOutboxRowSource<OutboxMessageEntity, IntegrationEventMessage>, OutboxMessageRowSource>();
        services.TryAddSingleton<Bsft.Outbox.IOutboxStore<IntegrationEventMessage>>(sp =>
            new global::Bsft.Outbox.EFCore.EFCoreOutboxStore<OutboxMessageEntity, IntegrationEventMessage>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOutboxRowSource<OutboxMessageEntity, IntegrationEventMessage>>()));
        services.TryAddSingleton<IOutboxStore, EFCoreOutboxStore>();

        services.AddOptions<BsftMessagingOptions>()
            .Configure(options => configure?.Invoke(options.Outbox))
            .ValidateOnStart();

        // One dispatcher behind both roles: the host polls it as a background service, and an
        // on-demand drain through IOutboxProcessor goes through the same instance, so a test drains
        // the messages the application would have published rather than a second dispatcher over the
        // same outbox.
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<IOutboxProcessor>(sp => sp.GetRequiredService<OutboxDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

        return services;
    }
}
