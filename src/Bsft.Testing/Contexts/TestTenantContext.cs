namespace Bsft.Testing.Contexts;

/// <summary>
/// A tenant context a test controls directly, instead of having a tenant resolved from a claim, a
/// header, or a host name.
/// </summary>
/// <remarks>
/// <para>
/// A new instance is already resolved to <see cref="DefaultTenantId"/>, so tenant-filtered queries
/// return rows without any further setup. Call <see cref="Clear"/> to test the unresolved case.
/// </para>
/// <para>
/// <see cref="Bsft.Data.MultiTenancy.ITenantContext"/> is the framework's one tenant abstraction:
/// it drives the query filters and the save interceptor, and the tenant authorization handler reads
/// the same context.
/// </para>
/// </remarks>
public sealed class TestTenantContext : Bsft.Data.MultiTenancy.ITenantContext
{
    /// <summary>The tenant a default-constructed instance is resolved to.</summary>
    public const string DefaultTenantId = "test-tenant";

    /// <summary>
    /// Initializes a new instance of the <see cref="TestTenantContext"/> class resolved to
    /// <see cref="DefaultTenantId"/>.
    /// </summary>
    public TestTenantContext()
        : this(DefaultTenantId)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestTenantContext"/> class resolved to the given
    /// tenant.
    /// </summary>
    /// <param name="tenantId">The resolved tenant identifier.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tenantId"/> is null, empty, or white space.
    /// </exception>
    public TestTenantContext(string tenantId)
    {
        SetTenant(tenantId);
    }

    /// <inheritdoc />
    public string? TenantId { get; private set; }

    /// <inheritdoc />
    public bool IsResolved { get; private set; }

    /// <summary>Resolves the context to the given tenant, replacing the previous one.</summary>
    /// <param name="tenantId">The tenant identifier to resolve to.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="tenantId"/> is null, empty, or white space.
    /// </exception>
    public void SetTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("The tenant identifier must not be empty.", nameof(tenantId));

        TenantId = tenantId;
        IsResolved = true;
    }

    /// <summary>Returns the context to the unresolved state, as if no tenant had been detected.</summary>
    public void Clear()
    {
        TenantId = null;
        IsResolved = false;
    }
}
