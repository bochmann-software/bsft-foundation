namespace Bsft.Retention.Watermarks;

/// <summary>
/// Answers where each archive kept under a disk watermark stood at its last measurement on this
/// instance.
/// </summary>
/// <remarks>
/// Registered by <c>AddBsftRetentionDiskWatermark</c>. It is the seam a host builds a health check, a
/// dashboard or a metric of its own on; the framework-tier satellite <c>Bsft.Retention.HealthChecks</c>
/// reports from it. The answer is per
/// instance and as fresh as the last sweep.
/// </remarks>
public interface IDiskWatermarkStatusReader
{
    /// <summary>Finds the last snapshot of a watermark pass.</summary>
    /// <param name="passName">The pass, as <see cref="DiskWatermarkOptions.NameFor{TSource}"/> names it.</param>
    /// <returns>The last snapshot, or null when the pass has not measured yet.</returns>
    DiskWatermarkStatusInfo? Find(string passName);
}
