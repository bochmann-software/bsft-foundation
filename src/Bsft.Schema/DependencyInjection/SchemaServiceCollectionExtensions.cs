using Bsft.Schema.Reading;
using Bsft.Schema.ValueValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Schema.DependencyInjection;

/// <summary>Extension methods for registering the schema reader and validator.</summary>
public static class SchemaServiceCollectionExtensions
{
    /// <summary>Registers the reader and the validator, with the shipped bounds.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Adjusts the bounds a submitted schema and document are read within.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Both are singletons and both are stateless between calls, so a host may hold one and validate
    /// concurrently. Both are registered with <c>TryAdd</c>, so a host that registered its own first
    /// keeps it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftSchema(
        this IServiceCollection services, Action<BsftSchemaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Register(services, configuration: null, configure);
    }

    /// <summary>Registers the same services, with the bounds bound from <c>Bsft:Schema</c> first.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Schema</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddBsftSchema(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftSchemaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return Register(services, configuration, configure);
    }

    private static IServiceCollection Register(
        IServiceCollection services, IConfiguration? configuration, Action<BsftSchemaOptions>? configure)
    {
        services.AddOptions<BsftSchemaOptions>()
            .Configure(target =>
            {
                configuration?.GetSection(BsftSchemaOptions.SectionName).Bind(target);
                configure?.Invoke(target);
            })
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftSchemaOptions>, BsftSchemaOptionsValidator>());

        services.TryAddSingleton<ISchemaReader, SchemaReader>();
        services.TryAddSingleton<ISchemaValidator, SchemaValidator>();

        return services;
    }
}
