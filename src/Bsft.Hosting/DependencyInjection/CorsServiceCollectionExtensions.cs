using Bsft.Hosting.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering BSFT CORS services.
/// </summary>
public static class CorsServiceCollectionExtensions
{
    /// <summary>
    /// Adds CORS services with restrictive defaults (no origins allowed).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure <see cref="BsftCorsOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftCors(
        this IServiceCollection services,
        Action<BsftCorsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Add(services, configuration: null, configure);
    }

    /// <summary>
    /// Adds CORS services, bound from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>Bsft:Hosting:Cors</c> section.</param>
    /// <param name="configure">Adjusts what the section bound, and runs after it.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddBsftCors(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BsftCorsOptions>? configure = null)
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
        IServiceCollection services, IConfiguration? configuration, Action<BsftCorsOptions>? configure)
    {
        void Apply(BsftCorsOptions target)
        {
            configuration?.GetSection(BsftCorsOptions.SectionName).Bind(target);
            configure?.Invoke(target);
        }

        // The policy, the built-in evaluator and UseBsftCors share this call's own options, as the policy
        // always was built from them. The shared IOptions<BsftCorsOptions> below is registered as it always
        // was: bound and configured at resolution, accumulating over repeated calls.
        var options = new BsftCorsOptions();
        Apply(options);
        var registration = new BsftCorsRegistration(options);
        services.Replace(ServiceDescriptor.Singleton(registration));

        services.AddOptions<BsftCorsOptions>().Configure(Apply).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BsftCorsOptions>, BsftCorsOptionsValidator>());

        services.AddCors();
        services.TryAddSingleton<IBsftCorsOriginEvaluator, BsftCorsOriginEvaluator>();
        services.AddOptions<CorsOptions>().Configure<IBsftCorsOriginEvaluator>(
            (corsOptions, registered) => corsOptions.AddPolicy(options.PolicyName, builder =>
                ConfigurePolicy(builder, registration, registered)));

        return services;
    }

    private static void ConfigurePolicy(
        CorsPolicyBuilder builder, BsftCorsRegistration registration, IBsftCorsOriginEvaluator registered)
    {
        var options = registration.Options;
        if (options.ExposedHeaders.Count > 0)
        {
            builder.WithExposedHeaders([.. options.ExposedHeaders]);
        }

        if (options.IsAnyOriginAllowed)
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
            return;
        }

        if (options.Origins.Count > 0)
        {
            builder.WithOrigins([.. options.Origins]);
        }

        // ASP.NET Core adds Vary: Origin as soon as the origin decision is not its own default, so the
        // evaluator is installed only when it can decide differently: a predicate, or a host evaluator.
        if (options.OriginPredicate is not null || registered is not BsftCorsOriginEvaluator)
        {
            // A host evaluator is shared by every policy; the built-in decision is this call's own, so a
            // policy registered by an earlier call under another name stays consistent with its options.
            var decision = registered is BsftCorsOriginEvaluator
                ? new BsftCorsOriginEvaluator(registration)
                : registered;
            builder.SetIsOriginAllowed(decision.IsOriginAllowed);
        }

        builder.AllowAnyMethod()
               .AllowAnyHeader();

        if (options.AreCredentialsAllowed)
        {
            builder.AllowCredentials();
        }
    }
}
