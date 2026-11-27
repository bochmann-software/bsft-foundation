using Bsft.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// The scope one step of a background pass runs in: the tenant's, opened through the host's
/// <see cref="ITenantScopeFactory"/>, while the pass works for a tenant, and a plain one otherwise.
/// </summary>
/// <remarks>
/// The tenant's scope is opened with <see cref="ITenantScopeFactory.CreateScopeAsync"/>, so a host that binds it with
/// I/O (a leased data source, a directory lookup) is never called synchronously, and both kinds of scope are disposed
/// asynchronously, so a service in them that is only <see cref="IAsyncDisposable"/> is released without a fault.
/// </remarks>
internal readonly struct TenantPassScope : IAsyncDisposable
{
    private readonly ITenantScope? _tenantScope;
    private readonly AsyncServiceScope _scope;

    private TenantPassScope(ITenantScope tenantScope) => _tenantScope = tenantScope;

    private TenantPassScope(AsyncServiceScope scope) => _scope = scope;

    /// <summary>Gets the provider of the scope's services.</summary>
    public IServiceProvider ServiceProvider => _tenantScope?.ServiceProvider ?? _scope.ServiceProvider;

    /// <summary>Opens the scope for the tenant of the pass, if there is one and a factory to bind it.</summary>
    /// <param name="scopeFactory">Opens the plain scope outside a tenant's pass, or when the host has no tenant scope factory.</param>
    /// <param name="tenantScopes">Opens the tenant's scope, or <see langword="null"/> when the host registered none.</param>
    /// <param name="cancellationToken">A token to observe while the tenant's scope is being bound.</param>
    /// <returns>The scope, which the caller disposes asynchronously.</returns>
    public static async ValueTask<TenantPassScope> CreateAsync(
        IServiceScopeFactory scopeFactory, ITenantScopeFactory? tenantScopes, CancellationToken cancellationToken = default)
    {
        if (BackgroundTenant.Current is { } tenantId && tenantScopes is not null)
            return new TenantPassScope(await tenantScopes.CreateScopeAsync(tenantId, cancellationToken).ConfigureAwait(false));

        return new TenantPassScope(scopeFactory.CreateAsyncScope());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _tenantScope?.DisposeAsync() ?? _scope.DisposeAsync();
}
