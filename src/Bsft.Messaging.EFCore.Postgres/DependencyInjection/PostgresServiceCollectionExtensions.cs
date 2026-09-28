using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Postgres.DependencyInjection;

/// <summary>
/// Extension methods for registering the PostgreSQL wake-up of the EF Core outbox.
/// </summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Adds the notification half of the outbox: the <see cref="OutboxNotifyInterceptor"/> that
    /// announces a save which wrote outbox rows, and the worker that listens for those announcements
    /// and drains the outbox at once instead of waiting for the dispatcher's next poll.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext type implementing <see cref="IOutboxDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftMessagingPostgresOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Call it alongside <c>AddBsftMessagingEFCore</c>, never instead of it: the dispatcher that call
    /// registers is what delivers, and its polling stays in place as the guarantee behind every
    /// notification that does not arrive. Then add the interceptor to the context, next to the outbox
    /// interceptor the core package registers:
    /// </para>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;((sp, options) => options
    ///     .UseNpgsql(connectionString)
    ///     .AddInterceptors(
    ///         sp.GetRequiredService&lt;OutboxInterceptor&gt;(),
    ///         sp.GetRequiredService&lt;OutboxNotifyInterceptor&gt;()));
    /// </code>
    /// </remarks>
    public static IServiceCollection AddBsftMessagingEFCorePostgres<TContext>(
        this IServiceCollection services, Action<BsftMessagingPostgresOptions>? configure = null)
        where TContext : DbContext, IOutboxDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        return Register<TContext>(services, configuration: null, configure);
    }

    /// <summary>
    /// The overload that binds <see cref="BsftMessagingPostgresOptions.SectionName"/> from
    /// configuration before the action runs, so a deployment states the channel and the listening
    /// connection in <c>appsettings.json</c> and the code states only what a section cannot hold.
    /// </summary>
    /// <typeparam name="TContext">The application's DbContext type implementing <see cref="IOutboxDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is bound from.</param>
    /// <param name="configure">An optional action applied after the section was bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    public static IServiceCollection AddBsftMessagingEFCorePostgres<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftMessagingPostgresOptions>? configure = null)
        where TContext : DbContext, IOutboxDbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        return Register<TContext>(services, configuration, configure);
    }

    private static IServiceCollection Register<TContext>(
        IServiceCollection services, IConfiguration? configuration, Action<BsftMessagingPostgresOptions>? configure)
        where TContext : DbContext, IOutboxDbContext
    {
        var options = services.AddOptions<BsftMessagingPostgresOptions>();

        if (configuration is not null)
            options.Bind(configuration.GetSection(BsftMessagingPostgresOptions.SectionName));

        options.Configure(bound => configure?.Invoke(bound)).ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<BsftMessagingPostgresOptions>, BsftMessagingPostgresOptionsValidator>());

        // The notifier is stateless and takes the context it writes through as an argument, so it is
        // shared; the interceptor is scoped because that is the lifetime a DbContext resolves its
        // interceptors at.
        services.TryAddSingleton<IOutboxNotifier, PostgresOutboxNotifier>();
        services.TryAddScoped<OutboxNotifyInterceptor>();

        services.TryAddSingleton<IOutboxNotificationChannel, PostgresOutboxNotificationChannel<TContext>>();
        services.AddHostedService<OutboxNotificationWorker>();

        return services;
    }
}
