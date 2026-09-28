using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Tells a freshly opened service scope which tenant the work inside it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Background work — a sweeper pass, a storage-layer hook, an event handler — runs outside any
/// request, so the scope it opens gets an unresolved <see cref="ITenantContext"/> and every
/// tenant-predicated read in it answers for no tenant. The work knows the tenant anyway, from a row
/// it read or an event it received, and this is how it says so.
/// </para>
/// <para>
/// Saying so requires the shipped <see cref="TenantContext"/>, the only implementation that can be
/// told what it is. A host that registered a read-only context of its own is refused loudly rather
/// than served silently under the wrong tenant or none: metering the wrong customer, notifying the
/// wrong roster or deleting the wrong tenant's object are all worse than work that does not run.
/// </para>
/// <para>
/// Written once here because three packages had grown their own near-identical copy —
/// <c>Bsft.Billing.Storage</c>, <c>Bsft.Chat.Notifications</c> and <c>Bsft.Chat.Storage</c> — and
/// their null handling had already started to diverge, which is exactly the sort of drift that ends
/// with one of them entering the empty string as if it were a tenant.
/// </para>
/// </remarks>
public static class TenantWorkScopeExtensions
{
    /// <summary>Reads the tenant the scope is currently serving, if it has one.</summary>
    /// <param name="services">The scope's services.</param>
    /// <returns>
    /// The tenant identifier, or <see langword="null"/> when the host registered no
    /// <see cref="ITenantContext"/> at all or the context is unresolved.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Resolved optionally on purpose: multi-tenancy is opt-in, and a host without it should record no
    /// tenant rather than fail. That is the same shape its keys and rows are in.
    /// </remarks>
    public static string? CurrentTenant(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetService<ITenantContext>()?.TenantId;
    }

    /// <summary>States the tenant of the work being done in this scope.</summary>
    /// <param name="services">The scope's services.</param>
    /// <param name="tenantId">
    /// The tenant the work belongs to, or <see langword="null"/> or empty when it belongs to none —
    /// which is the ordinary state of a host without multi-tenancy, not a failure.
    /// </param>
    /// <param name="work">
    /// What the work is, in a few words, so the failure below names the thing that could not run
    /// instead of the framework. For example <c>"The chat attachment sweep"</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="work"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a tenant has to be stated but the registered <see cref="ITenantContext"/> cannot be
    /// told which tenant it is.
    /// </exception>
    public static void EnterTenant(this IServiceProvider services, string? tenantId, string work)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(work);

        // Empty counts as absent: a context told that it is resolved to "" is in a worse state than an
        // unresolved one, because everything downstream then reads it as a real tenant.
        if (string.IsNullOrEmpty(tenantId))
            return;

        SettableTenantContext(services, tenantId, work).SetTenantId(tenantId);
    }

    /// <summary>Proves the scope's context could be told a tenant, without telling it one.</summary>
    /// <param name="services">The scope's services.</param>
    /// <param name="work">What the work is, for the failure message.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="work"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the registered <see cref="ITenantContext"/> cannot be told which tenant it is.
    /// </exception>
    /// <remarks>
    /// It exists so a startup check can raise that failure at deployment rather than at the first
    /// piece of background work, and it deliberately does not set a placeholder tenant to find out: a
    /// context marked resolved to a tenant that does not exist is worse than an unresolved one.
    /// </remarks>
    public static void RequireSettableTenantContext(this IServiceProvider services, string work)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(work);

        _ = SettableTenantContext(services, tenantId: null, work);
    }

    /// <summary>Resolves the scope's tenant context and insists it can be written to.</summary>
    /// <param name="services">The scope's services.</param>
    /// <param name="tenantId">The tenant that has to be entered, quoted in the failure, if any.</param>
    /// <param name="work">What the work is, for the failure message.</param>
    /// <returns>The settable context.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the registered context is not settable.</exception>
    private static TenantContext SettableTenantContext(IServiceProvider services, string? tenantId, string work)
    {
        var context = services.GetService<ITenantContext>();

        if (context is TenantContext settable)
            return settable;

        var registered = context?.GetType().FullName ?? "none";
        var subject = tenantId is null ? "the tenant it is serving" : $"that it is serving tenant '{tenantId}'";

        throw new InvalidOperationException(
            $"{work} has to tell its own scope {subject}, but the registered ITenantContext is '{registered}', "
            + "which cannot be told. Register Bsft.Data's TenantContext, which AddBsftDataMultiTenancy does.");
    }
}
