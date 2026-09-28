using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Calendar.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the business calendar.
/// </summary>
public static class CalendarServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBusinessCalendarProvider"/> and the configured calendars.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeated calls are additive: each one contributes another configuration step to the same
    /// options instance. Calendars are a keyed registry that several packages contribute to — a
    /// package registering its defaults must not silently discard the calendars the application
    /// registers afterwards.
    /// </para>
    /// <para>
    /// There is no <c>IConfiguration</c> overload, because <see cref="BsftCalendarOptions"/> holds
    /// calendar implementations rather than values a configuration provider could bind.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback to register keyed calendars or replace the default one.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCalendar(
        this IServiceCollection services,
        Action<BsftCalendarOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddOptions<BsftCalendarOptions>();

        if (configure is not null)
            builder.Configure(configure);

        builder.ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftCalendarOptions>, BsftCalendarOptionsValidator>());

        services.TryAddSingleton<IBusinessCalendarProvider, BusinessCalendarProvider>();
        return services;
    }
}
