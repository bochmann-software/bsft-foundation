namespace Bsft.Data;

/// <summary>
/// The constants a host needs when it wires the data layer, so that the value a resolver falls back
/// to and the value a client sends are stated in one place rather than repeated as literals.
/// </summary>
public static class DataDefaults
{
    /// <summary>
    /// The request header a tenant identifier is read from when the host names no other.
    /// </summary>
    public const string TenantHeaderName = "X-Tenant-Id";

    /// <summary>
    /// The claim type a tenant identifier is read from when the host names no other. It is also the
    /// canonical tenant claim the authentication stack maps onto, so changing it changes both ends.
    /// </summary>
    public const string TenantClaimType = "tenant_id";
}
