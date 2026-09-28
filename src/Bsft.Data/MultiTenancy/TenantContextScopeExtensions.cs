using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Writes the ambient tenant onto a scope's own <see cref="ITenantContext"/>, for a bridge that opens
/// a scope with no request behind it and has to state which tenant it is serving itself.
/// </summary>
/// <remarks>
/// Every scoped store in <c>Bsft.Data</c>-backed packages reads <see cref="ITenantContext"/>, and the
/// shipped <see cref="TenantContext"/> is the only implementation a caller outside a request can tell
/// what it is: it is what <c>AddBsftDataMultiTenancy</c> registers, and the one type this method knows
/// how to set. A host that substituted a read-only <see cref="ITenantContext"/> of its own is refused
/// loudly here rather than left to run every read of the scope against no tenant at all.
/// </remarks>
public static class TenantContextScopeExtensions
{
    /// <summary>States the tenant a scope is serving, on the scope's own <see cref="ITenantContext"/>.</summary>
    /// <param name="services">The scope's own services.</param>
    /// <param name="tenantId">The tenant to state.</param>
    /// <param name="describeFailure">
    /// Builds the message for a context that cannot be told, given what was resolved (or
    /// <see langword="null"/> when nothing was registered at all).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="tenantId"/> or <paramref name="describeFailure"/>
    /// is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">The registered context cannot be told which tenant it is.</exception>
    public static void EnterTenantScope(
        this IServiceProvider services,
        string tenantId,
        Func<ITenantContext?, string> describeFailure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(describeFailure);

        var context = services.GetService<ITenantContext>();

        if (context is TenantContext settable)
        {
            settable.SetTenantId(tenantId);
            return;
        }

        throw new InvalidOperationException(describeFailure(context));
    }
}
