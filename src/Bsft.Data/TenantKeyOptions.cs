namespace Bsft.Data;

/// <summary>
/// How the ids of the tenant-keyed framework tables are made, for a host that maps them with
/// <c>UseTenantKeys</c> and registered <c>AddBsftDataTenantKeys</c>.
/// </summary>
/// <remarks>
/// A nested group of <see cref="BsftDataOptions"/>, bound as part of its section
/// (<c>Bsft:Data:TenantKeys</c>) where a host binds that section.
/// </remarks>
public sealed class TenantKeyOptions
{
    /// <summary>
    /// Gets or sets the node id, from 0 to 4095, stamped into every id this process makes, or
    /// <see langword="null"/> for one per clock, counted up from a start drawn at random per process.
    /// </summary>
    /// <remarks>
    /// Ids of one process never collide. Ids of two processes never collide when their node ids differ,
    /// so a host that runs several processes against one database gives each its own, for instance from
    /// the replica ordinal. Two processes that happen to share a node id can make the same id in the same
    /// millisecond; the insert then fails on the primary key rather than overwriting a row.
    /// </remarks>
    public int? NodeId { get; set; }
}
