namespace Bsft.Data;

/// <summary>
/// Where a request carries its tenant identifier: the header one resolver reads and the claim type
/// another reads.
/// </summary>
/// <remarks>
/// This group holds only the <em>values</em> a resolver needs. Which resolver runs is chosen in code
/// through <see cref="MultiTenancy.MultiTenancyConfiguration"/>, because that choice selects a type —
/// including one the application wrote itself — and a configuration provider supplies strings, not
/// types. Both members are read regardless of the choice, so a deployment can state the header name
/// in its settings and still switch the strategy in code without the two contradicting each other.
/// </remarks>
public sealed class MultiTenancyOptions
{
    /// <summary>
    /// Gets or sets the request header the header resolver reads the tenant identifier from.
    /// Defaults to <see cref="DataDefaults.TenantHeaderName"/>.
    /// </summary>
    public string HeaderName { get; set; } = DataDefaults.TenantHeaderName;

    /// <summary>
    /// Gets or sets the claim type the claim resolver reads the tenant identifier from.
    /// Defaults to <see cref="DataDefaults.TenantClaimType"/>.
    /// </summary>
    public string ClaimType { get; set; } = DataDefaults.TenantClaimType;
}
