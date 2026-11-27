namespace Bsft.Retention.Watermarks;

/// <summary>What one sweep of a disk watermark deletes, and what that makes of the watermark.</summary>
/// <param name="Evictions">The items to delete, oldest first.</param>
/// <param name="PlannedBytes">How many bytes those items hold.</param>
/// <param name="RequiredBytes">How many bytes have to go for both limits to reach their target.</param>
/// <param name="State">
/// <see cref="DiskWatermarkState.Within"/> when nothing has to go, <see cref="DiskWatermarkState.Draining"/>
/// when the evictions free enough, <see cref="DiskWatermarkState.Unreachable"/> when they do not.
/// </param>
internal sealed record DiskWatermarkPlanResult(
    IReadOnlyList<DiskWatermarkItemInfo> Evictions,
    long PlannedBytes,
    long RequiredBytes,
    DiskWatermarkState State);
