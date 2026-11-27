using Bsft.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Finds the tenant a row of a tenant-keyed table is written for, from the scope that writes it.
/// </summary>
/// <remarks>
/// The scope's <see cref="ITenantContext"/> wins when it resolved a tenant; otherwise the tenant of the
/// running background pass (<see cref="BackgroundTenant.Current"/>) is used. See <c>docs/data.md</c>,
/// "Tenant keys".
/// </remarks>
public static class TenantKeyScopeExtensions
{
    /// <summary>
    /// Returns the tenant a row written in a scope belongs to: the scope's <see cref="ITenantContext"/>
    /// when it resolved one, otherwise the tenant of the running background pass.
    /// </summary>
    /// <param name="scopedServices">The scope's services, or <see langword="null"/>.</param>
    /// <returns>The tenant id, or <see langword="null"/> when the scope has none.</returns>
    public static string? FindCurrentTenantId(this IServiceProvider? scopedServices) =>
        (scopedServices?.GetService<ITenantContext>()).FindCurrentTenantId();

    /// <summary>
    /// Returns the tenant a row written in a scope belongs to: the given <see cref="ITenantContext"/>
    /// when it resolved one, otherwise the tenant of the running background pass.
    /// </summary>
    /// <param name="tenantContext">The scope's tenant context, or <see langword="null"/>.</param>
    /// <returns>The tenant id, or <see langword="null"/> when the scope has none.</returns>
    public static string? FindCurrentTenantId(this ITenantContext? tenantContext) =>
        tenantContext is { TenantId: { Length: > 0 } tenantId } ? tenantId : BackgroundTenant.Current;

    /// <summary>
    /// Returns the tenant a row written in a scope belongs to, and refuses a scope that has none.
    /// </summary>
    /// <param name="scopedServices">The scope's services, or <see langword="null"/>.</param>
    /// <param name="table">The table the row is written to, for the message.</param>
    /// <returns>The tenant id.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the scope has no tenant.</exception>
    public static string GetCurrentTenantId(this IServiceProvider? scopedServices, string table) =>
        (scopedServices?.GetService<ITenantContext>()).GetCurrentTenantId(table);

    /// <summary>
    /// Returns the tenant a row written in a scope belongs to, and refuses a scope that has none.
    /// </summary>
    /// <param name="tenantContext">The scope's tenant context, or <see langword="null"/>.</param>
    /// <param name="table">The table the row is written to, for the message.</param>
    /// <returns>The tenant id.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the scope has no tenant.</exception>
    public static string GetCurrentTenantId(this ITenantContext? tenantContext, string table) =>
        tenantContext.FindCurrentTenantId()
            ?? throw new InvalidOperationException(
                $"Cannot use the tenant-keyed table '{table}' because no tenant is resolved in the current scope. "
                + "A request resolves one through ITenantContext; a background pass runs inside a BackgroundTenant.");
}
