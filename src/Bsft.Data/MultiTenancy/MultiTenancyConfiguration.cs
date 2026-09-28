using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Selects how the current tenant is resolved from a request.
/// </summary>
/// <remarks>
/// This is a registration builder, not an options type. What it decides is a <em>type</em> — including
/// one the application wrote itself — which a configuration provider cannot supply. The values the
/// chosen resolver reads live on <see cref="MultiTenancyOptions"/> instead, and a name stated here is
/// applied to them with <c>PostConfigure</c>, so that it wins over the configuration section.
/// </remarks>
public sealed class MultiTenancyConfiguration
{
    private Action<IServiceCollection>? _resolverRegistration;
    private Action<MultiTenancyOptions>? _optionsAdjustment;

    /// <summary>
    /// Configures tenant resolution from an HTTP header.
    /// </summary>
    /// <param name="headerName">
    /// The header name to read the tenant from, or <see langword="null"/> to take the configured
    /// value, which defaults to <see cref="DataDefaults.TenantHeaderName"/>.
    /// </param>
    /// <returns>This configuration for chaining.</returns>
    public MultiTenancyConfiguration ResolveFromHeader(string? headerName = null)
    {
        _optionsAdjustment = headerName is null ? null : options => options.HeaderName = headerName;
        _resolverRegistration = static services => services.AddSingleton<ITenantResolver, HeaderTenantResolver>();
        return this;
    }

    /// <summary>
    /// Configures tenant resolution from a user claim.
    /// </summary>
    /// <param name="claimType">
    /// The claim type to read the tenant from, or <see langword="null"/> to take the configured value,
    /// which defaults to <see cref="DataDefaults.TenantClaimType"/>.
    /// </param>
    /// <returns>This configuration for chaining.</returns>
    public MultiTenancyConfiguration ResolveFromClaim(string? claimType = null)
    {
        _optionsAdjustment = claimType is null ? null : options => options.ClaimType = claimType;
        _resolverRegistration = static services => services.AddSingleton<ITenantResolver, ClaimTenantResolver>();
        return this;
    }

    /// <summary>
    /// Configures tenant resolution from the request subdomain.
    /// </summary>
    /// <returns>This configuration for chaining.</returns>
    public MultiTenancyConfiguration ResolveFromSubdomain()
    {
        _optionsAdjustment = null;
        _resolverRegistration = static services => services.AddSingleton<ITenantResolver, SubdomainTenantResolver>();
        return this;
    }

    /// <summary>
    /// Configures a custom tenant resolver implementation.
    /// </summary>
    /// <typeparam name="TResolver">The resolver type.</typeparam>
    /// <returns>This configuration for chaining.</returns>
    public MultiTenancyConfiguration ResolveUsing<TResolver>() where TResolver : class, ITenantResolver
    {
        _optionsAdjustment = null;
        _resolverRegistration = static services => services.AddSingleton<ITenantResolver, TResolver>();
        return this;
    }

    /// <summary>
    /// Applies a name stated in code to an options instance, so the registration call can validate the
    /// same value the container will later produce.
    /// </summary>
    /// <param name="options">The options being built.</param>
    internal void ApplyOptions(BsftDataOptions options) => _optionsAdjustment?.Invoke(options.MultiTenancy);

    /// <summary>
    /// Applies the resolver registration, and any name stated in code, to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal void Apply(IServiceCollection services)
    {
        if (_optionsAdjustment is { } adjustment)
            services.PostConfigure<BsftDataOptions>(options => adjustment(options.MultiTenancy));

        _resolverRegistration?.Invoke(services);
    }
}
