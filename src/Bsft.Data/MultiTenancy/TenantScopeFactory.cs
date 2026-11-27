using Bsft.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Creates a scope with the tenant context of <c>Bsft.Data</c> set to one tenant: the
/// <see cref="ITenantScopeFactory"/> that <c>AddBsftDataMultiTenancy</c> registers unless the host registered one.
/// </summary>
internal sealed class TenantScopeFactory : ITenantScopeFactory
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Initializes a new instance of the <see cref="TenantScopeFactory"/> class.</summary>
    /// <param name="scopes">The root scope factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopes"/> is null.</exception>
    public TenantScopeFactory(IServiceScopeFactory scopes)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    /// <inheritdoc />
    public ITenantScope CreateScope(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("A tenant scope is created for a named tenant.", nameof(tenantId));

        var scope = _scopes.CreateAsyncScope();

        try
        {
            scope.ServiceProvider.EnterTenant(tenantId, "A tenant scope");
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        return new TenantServiceScope(tenantId, scope);
    }

    /// <summary>The scope handed out, disposing the service scope it wraps.</summary>
    private sealed class TenantServiceScope(string tenantId, AsyncServiceScope scope) : ITenantScope
    {
        /// <inheritdoc />
        public string TenantId { get; } = tenantId;

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => scope.ServiceProvider;

        /// <inheritdoc />
        public void Dispose() => scope.Dispose();

        /// <inheritdoc />
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
