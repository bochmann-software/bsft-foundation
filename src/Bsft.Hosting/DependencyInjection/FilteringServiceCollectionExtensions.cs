using Bsft.Hosting.Filtering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Registers the CRUD list convention's filter transport: the model binder that reads the
/// <c>filter</c> query parameter into <c>PagedRequest.Filter</c>, and the bounded parser behind it.
/// </summary>
public static class FilteringServiceCollectionExtensions
{
    /// <summary>
    /// Registers the filter query transport and validates its bounds eagerly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftFilterTransportOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured bounds cannot be used.</exception>
    public static IServiceCollection AddBsftFilterTransport(
        this IServiceCollection services,
        Action<BsftFilterTransportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Registers the filter query transport with its bounds bound from configuration, and validates them
    /// eagerly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Filtering</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured bounds cannot be used.</exception>
    public static IServiceCollection AddBsftFilterTransport(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftFilterTransportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Add(services, configuration, configure);
    }

    /// <summary>Binds, validates and registers, whichever overload was called.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind from, or <see langword="null"/>.</param>
    /// <param name="configure">Adjusts what the section bound.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the configured bounds cannot be used.</exception>
    /// <remarks>
    /// The bounds are validated inside this call as well as through the registered
    /// <see cref="IValidateOptions{TOptions}"/>, because a bound the parser cannot work with should be a
    /// failure of the registration rather than of the first request that carries a filter.
    /// </remarks>
    internal static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftFilterTransportOptions>? configure)
    {
        void Apply(BsftFilterTransportOptions target)
        {
            configuration?.GetSection(BsftFilterTransportOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftFilterTransportOptions();
        Apply(options);
        options.Validate();

        services.AddOptions<BsftFilterTransportOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftFilterTransportOptions>,
                BsftFilterTransportOptionsValidator>());

        services.TryAddSingleton<IFilterQueryParser, FilterQueryParser>();

        // Insert the provider ahead of the defaults so it claims FilterNode before the complex-object
        // binder does. Configuring MvcOptions rather than calling AddControllers keeps this call
        // order-independent: it contributes the provider whether the host adds controllers before or
        // after AddBsftDefaults, and stays inert if the host never adds MVC at all.
        services.Configure<MvcOptions>(mvc =>
        {
            if (!mvc.ModelBinderProviders.OfType<FilterNodeModelBinderProvider>().Any())
                mvc.ModelBinderProviders.Insert(0, new FilterNodeModelBinderProvider(new FilterNodeModelBinder()));
        });

        return services;
    }
}
