namespace Bsft.Retention.Watermarks;

/// <summary>One measurement of an archive and its volume, as the planner reads it.</summary>
/// <param name="ArchiveBytes">
/// The sum of the sizes of every item the source listed, or null when the archive was not listed because
/// no archive limit is configured and the volume alone decided.
/// </param>
/// <param name="AvailableBytes">
/// The bytes available on the volume, or null when no floor is configured and no volume was measured.
/// </param>
/// <param name="Evictable">The items old enough to delete, oldest first.</param>
/// <param name="IsDraining">Whether a drain of an earlier sweep has not reached its target yet.</param>
internal sealed record DiskWatermarkPlanRequest(
    long? ArchiveBytes,
    long? AvailableBytes,
    IReadOnlyList<DiskWatermarkItemInfo> Evictable,
    bool IsDraining);
