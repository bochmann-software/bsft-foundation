using Bsft.Data.MultiTenancy;
using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Scope seed that carries the tenant context into a fresh scope during retry.</summary>
public sealed class TenantRetryLogScopeSeed(ITenantContext tenantContext) : IRequestScopeSeed
{
    public void Seed(IServiceProvider serviceProvider)
    {
        if (tenantContext.IsResolved && tenantContext.TenantId is not null)
        {
            serviceProvider.EnterTenantScope(tenantContext.TenantId, _ => "Failed to set tenant in fresh scope");
        }
    }
}
