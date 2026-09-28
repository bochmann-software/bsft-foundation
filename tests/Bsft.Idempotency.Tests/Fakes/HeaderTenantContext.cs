using Bsft.Data.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>
/// Resolves the tenant from an <c>X-Tenant</c> request header, so one host can serve two tenants and
/// the cross-tenant guarantee can be asserted from the outside.
/// </summary>
internal sealed class HeaderTenantContext : ITenantContext
{
    /// <summary>The header the tenant travels in.</summary>
    public const string HeaderName = "X-Tenant";

    private readonly IHttpContextAccessor _accessor;

    public HeaderTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? TenantId
    {
        get
        {
            var value = _accessor.HttpContext?.Request.Headers[HeaderName].ToString();

            return string.IsNullOrEmpty(value) ? null : value;
        }
    }

    public bool IsResolved => TenantId is not null;
}
