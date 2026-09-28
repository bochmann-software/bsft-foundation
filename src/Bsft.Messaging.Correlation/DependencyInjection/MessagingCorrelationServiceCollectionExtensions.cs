using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Messaging.Correlation.DependencyInjection;

/// <summary>
/// Dependency injection extensions that bridge <c>Bsft.Correlation</c> and messaging.
/// </summary>
public static class MessagingCorrelationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the correlation accessor used to stamp a correlation id onto a staged integration
    /// event, so it can be restored when the event is handled.
    /// </summary>
    /// <remarks>
    /// Registered with a plain <c>Add</c> rather than <c>TryAdd</c> so that it replaces the
    /// ambient-only default from <c>AddBsftMessaging</c> whichever order the two are called in. It is
    /// scoped because <c>ICorrelationContext</c> is.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftMessagingCorrelation(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        services.AddScoped<IMessageCorrelationAccessor, CorrelationContextMessageAccessor>();
        return services;
    }
}
