using Bsft.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>
/// The tenant scope factory of a host that can only bind a tenant's scope asynchronously, as a consuming application's does:
/// <see cref="CreateScope"/> refuses, and a scope it hands out refuses a synchronous dispose.
/// </summary>
/// <param name="scopes">Opens the scopes the tenant scopes wrap.</param>
internal sealed class AsyncOnlyTenantScopeFactory(IServiceScopeFactory scopes) : ITenantScopeFactory
{
    private readonly List<string> _boundFor = [];
    private int _asyncDisposals;

    /// <summary>Gets or sets the tenant whose binding fails, or <see langword="null"/> when every binding succeeds.</summary>
    public string? FailingTenant { get; set; }

    /// <summary>Gets the tenants a scope was bound for, in order, including a failed binding.</summary>
    public IReadOnlyList<string> BoundFor
    {
        get
        {
            lock (_boundFor)
                return [.. _boundFor];
        }
    }

    /// <summary>Gets how many scopes were disposed asynchronously.</summary>
    public int AsyncDisposals => Volatile.Read(ref _asyncDisposals);

    /// <inheritdoc />
    public ITenantScope CreateScope(string tenantId) =>
        throw new NotSupportedException("This host binds a tenant's scope asynchronously; use CreateScopeAsync.");

    /// <inheritdoc />
    public async ValueTask<ITenantScope> CreateScopeAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_boundFor)
            _boundFor.Add(tenantId);

        if (tenantId == FailingTenant)
            throw new InvalidOperationException($"The scope of tenant '{tenantId}' could not be bound.");

        var scope = scopes.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<BoundTenant>() is { } bound)
            bound.TenantId = tenantId;

        return new Scope(this, tenantId, scope);
    }

    /// <summary>A scoped marker the factory sets to the tenant it bound the scope for.</summary>
    public sealed class BoundTenant
    {
        /// <summary>Gets or sets the tenant the scope was bound for, or <see langword="null"/> in a plain scope.</summary>
        public string? TenantId { get; set; }
    }

    private sealed class Scope(AsyncOnlyTenantScopeFactory owner, string tenantId, AsyncServiceScope scope) : ITenantScope
    {
        public string TenantId => tenantId;

        public IServiceProvider ServiceProvider => scope.ServiceProvider;

        public void Dispose() =>
            throw new InvalidOperationException("A tenant scope of this host is disposed asynchronously.");

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref owner._asyncDisposals);
            await scope.DisposeAsync();
        }
    }
}
