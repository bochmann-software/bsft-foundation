using Bsft.Hosting.Cors;
using Bsft.Hosting.Filtering;
using Bsft.Hosting.Https;
using Bsft.Hosting.Logging;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>
/// Fluent configuration for all BSFT hosting defaults, allowing selective customization of individual components.
/// </summary>
/// <remarks>
/// This is a registration builder, not an options type: it holds the <c>Action&lt;T&gt;</c> each component
/// is configured with, and it carries no <c>SectionName</c> because a delegate is not something a
/// configuration provider can supply. Each component's values live on its own options type, in its own
/// <c>Bsft:Hosting:*</c> section.
/// </remarks>
public sealed class BsftDefaultsConfiguration
{
    internal Action<BsftSwaggerOptions>? SwaggerConfigure { get; private set; }

    internal Action<BsftVersioningOptions>? VersioningConfigure { get; private set; }

    internal Action<BsftCorsOptions>? CorsConfigure { get; private set; }

    internal Action<BsftRateLimitingOptions>? RateLimitingConfigure { get; private set; }

    internal Action<BsftCompressionOptions>? CompressionConfigure { get; private set; }

    internal Action<BsftJsonOptions>? JsonConfigure { get; private set; }

    internal Action<BsftFilterTransportOptions>? FilterTransportConfigure { get; private set; }

    internal Action<BsftHostLoggingOptions>? LoggingConfigure { get; private set; }

    internal bool IsHttpsRedirectionDisabled { get; private set; }

    /// <summary>
    /// Configures Swagger/OpenAPI options.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftSwaggerOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration Swagger(Action<BsftSwaggerOptions> configure)
    {
        SwaggerConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures API versioning options.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftVersioningOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration ApiVersioning(Action<BsftVersioningOptions> configure)
    {
        VersioningConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures CORS options.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftCorsOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration Cors(Action<BsftCorsOptions> configure)
    {
        CorsConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures rate limiting options.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftRateLimitingOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration RateLimiting(Action<BsftRateLimitingOptions> configure)
    {
        RateLimitingConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures response compression options.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftCompressionOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration Compression(Action<BsftCompressionOptions> configure)
    {
        CompressionConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures JSON serialization options.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftJsonOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration Json(Action<BsftJsonOptions> configure)
    {
        JsonConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures the CRUD list convention's filter transport bounds: the maximum length of the raw
    /// <c>filter</c> query value and the depth and node-count limits enforced on the parsed AST.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftFilterTransportOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration FilterTransport(Action<BsftFilterTransportOptions> configure)
    {
        FilterTransportConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Configures host logging options: per-category levels, the console and debug providers, and
    /// the nested file logger configuration.
    /// </summary>
    /// <param name="configure">An action to configure <see cref="BsftHostLoggingOptions"/>.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration Logging(Action<BsftHostLoggingOptions> configure)
    {
        LoggingConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>
    /// Disables HTTPS redirection in the middleware pipeline.
    /// </summary>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftDefaultsConfiguration DisableHttpsRedirection()
    {
        IsHttpsRedirectionDisabled = true;
        return this;
    }

    /// <summary>
    /// Carries <see cref="DisableHttpsRedirection"/> into the HTTPS options, and only then: a host that
    /// never called it keeps whatever the section bound, so configuration is not silently overwritten by
    /// a default the caller never expressed.
    /// </summary>
    /// <param name="target">The options instance to adjust.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
    internal void ApplyHttps(BsftHttpsOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (IsHttpsRedirectionDisabled)
            target.RedirectionMode = HttpsRedirectionMode.Disabled;
    }
}
