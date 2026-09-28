namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Implemented by a <see cref="Microsoft.EntityFrameworkCore.DbContext"/> that exposes the tenant its
/// own instance was created for, so the global query filter can read the tenant off the context
/// instead of closing over one <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core caches the model, and with it every query filter expression, per context type. A filter
/// that captures an <see cref="ITenantContext"/> therefore keeps the first scope's instance forever.
/// A filter that reads a member of the context is rewritten by EF Core to the context executing the
/// query, which is why this indirection exists rather than a simpler accessor delegate.
/// </para>
/// <para>
/// The usual implementation forwards to the scoped <see cref="ITenantContext"/> the context was
/// constructed with:
/// </para>
/// <code>
/// public string? CurrentTenantId => _tenantContext.TenantId;
/// </code>
/// </remarks>
public interface ITenantScopedDbContext
{
    /// <summary>
    /// Gets the tenant identifier this context instance is scoped to, or null if no tenant is resolved.
    /// </summary>
    string? CurrentTenantId { get; }
}
