using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT JSON serialization defaults.
/// </summary>
public static class JsonServiceCollectionExtensions
{
    /// <summary>
    /// Configures System.Text.Json globally with BSFT defaults (camelCase, enum as string, etc.).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftJsonOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftJsonDefaults(
        this IServiceCollection services,
        Action<BsftJsonOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Configures System.Text.Json globally with BSFT defaults, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Json</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftJsonDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftJsonOptions>? configure = null)
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
    internal static IServiceCollection Add(
        IServiceCollection services, IConfiguration? configuration, Action<BsftJsonOptions>? configure)
    {
        void Apply(BsftJsonOptions target)
        {
            configuration?.GetSection(BsftJsonOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        var options = new BsftJsonOptions();
        Apply(options);

        services.AddOptions<BsftJsonOptions>().Configure(Apply).ValidateDataAnnotations().ValidateOnStart();

        services.ConfigureHttpJsonOptions(jsonOptions =>
        {
            ConfigureJsonSerializerOptions(jsonOptions.SerializerOptions, options);
        });

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(jsonOptions =>
        {
            ConfigureJsonSerializerOptions(jsonOptions.JsonSerializerOptions, options);
        });

        return services;
    }

    private static void ConfigureJsonSerializerOptions(JsonSerializerOptions serializerOptions, BsftJsonOptions options)
    {
        serializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        serializerOptions.PropertyNameCaseInsensitive = options.UsesCaseInsensitiveDeserialization;

        if (options.ShouldOmitNullValues)
        {
            serializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        }

        if (options.ShouldSerializeEnumsAsString)
        {
            serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        }
    }
}
