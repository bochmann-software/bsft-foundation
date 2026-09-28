using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Bsft.Messaging.Consuming;
using Bsft.Messaging.RabbitMQ.Connections;
using Bsft.Messaging.RabbitMQ.Consuming;
using Bsft.Messaging.RabbitMQ.Publishing;

namespace Bsft.Messaging.RabbitMQ.DependencyInjection;

/// <summary>
/// Extension methods for registering the RabbitMQ transport.
/// </summary>
public static class RabbitMQServiceCollectionExtensions
{
    /// <summary>
    /// Registers RabbitMQ as the transport for integration events — both halves of it — replacing the
    /// in-memory bus and supplying the source the consumer host reads from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it <strong>before</strong> <c>AddBsftMessaging</c>. Everything the messaging core registers
    /// is registered with <c>TryAdd</c>, so the first <see cref="IMessageBus"/> in the collection wins;
    /// calling this afterwards leaves the in-memory bus in place and the application silently keeps
    /// delivering its integration events to itself. The same ordering decides the receiving half: the
    /// loopback transport registers an <see cref="IMessageSource"/> the same way, and whichever ran
    /// first is the one the host consumes from.
    /// </para>
    /// <para>
    /// The options are validated by <c>ValidateOnStart()</c> rather than on first use. A background
    /// dispatcher is the first thing to publish, and a missing password surfacing there would look like
    /// a transient delivery failure being retried rather than the misconfiguration it is.
    /// </para>
    /// <para>
    /// This registers the transport, not the consuming pipeline. Add <c>AddBsftConsumerHost</c> for the
    /// host that enumerates the source, and declare every consumed event with <c>Subscribe</c> here and
    /// with <c>AddEvent</c> there — the first binds the queue, the second resolves the name.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">A callback stating where the broker is and what the topology is called.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IServiceCollection AddBsftRabbitMQMessaging(
        this IServiceCollection services, Action<BsftRabbitMQOptions> configure)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        return AddCore(services, configure);
    }

    /// <summary>
    /// Registers the RabbitMQ transport with its options bound from configuration section
    /// <see cref="BsftRabbitMQOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="configure">
    /// An optional action applied after the section has been bound. The subscriptions are declared here
    /// rather than in configuration, because <see cref="BsftRabbitMQOptions.Subscribe"/> is what adds
    /// them.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <remarks>
    /// The section is bound first and the delegate runs after it, so code wins over configuration for
    /// anything both of them set. This is the overload to reach for: the credentials belong in
    /// configuration or a secret store rather than in code. Everything the other overload documents
    /// applies here as well.
    /// </remarks>
    public static IServiceCollection AddBsftRabbitMQMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftRabbitMQOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        return AddCore(services, options =>
        {
            configuration.GetSection(BsftRabbitMQOptions.SectionName).Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers everything both entry-point overloads register.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">What shapes the options, whatever it was built from.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// The resolved connection settings and topology are singletons built from the options once the
    /// container has them, so nothing re-derives a name or re-checks a value later and every component
    /// reads the same resolution.
    /// </remarks>
    private static IServiceCollection AddCore(
        IServiceCollection services, Action<BsftRabbitMQOptions> configure)
    {
        services.AddOptions<BsftRabbitMQOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IValidateOptions<BsftRabbitMQOptions>, BsftRabbitMQOptionsValidator>());

        services.TryAddSingleton(
            sp => new RabbitMQConnectionSettings(sp.GetRequiredService<IOptions<BsftRabbitMQOptions>>().Value));
        services.TryAddSingleton(
            sp => new RabbitMQTopology(sp.GetRequiredService<IOptions<BsftRabbitMQOptions>>().Value));

        // The source hands its clock to every delivery it presents, which is what a retry delay is
        // waited on. TryAdd, so a host that registered a clock of its own keeps it.
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IRabbitMQConnectionFactory, RabbitMQConnectionFactoryAdapter>();
        services.TryAddSingleton<IRabbitMQMessageMapper, RabbitMQMessageMapper>();
        services.TryAddSingleton<IRabbitMQTopologyDeclarer, RabbitMQTopologyDeclarer>();
        services.TryAddSingleton<IRabbitMQPublishChannelProvider, RabbitMQPublishChannelProvider>();
        services.TryAddSingleton<IMessageBus, RabbitMQMessageBus>();

        services.TryAddSingleton<IRabbitMQConsumerChannelFactory, RabbitMQConsumerChannelFactory>();
        services.TryAddSingleton<IRabbitMQParkPublisher, RabbitMQParkPublisher>();
        services.TryAddSingleton<IRabbitMQMessageConsumer, RabbitMQMessageConsumer>();
        services.TryAddSingleton<IMessageSource, RabbitMQMessageSource>();

        return services;
    }
}
