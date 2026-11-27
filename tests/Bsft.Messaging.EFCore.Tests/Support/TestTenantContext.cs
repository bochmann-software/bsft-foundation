using Bsft.Data.MultiTenancy;

namespace Bsft.Messaging.EFCore.Tests.Support;

/// <summary>A test implementation of <see cref="ITenantContext"/> that allows setting the current tenant.</summary>
internal sealed class TestTenantContext : ITenantContext
{
    /// <summary>Gets or sets the current tenant identifier.</summary>
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public bool IsResolved => !string.IsNullOrEmpty(TenantId);
}
