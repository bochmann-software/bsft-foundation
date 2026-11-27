namespace Bsft.Retention.Watermarks;

/// <summary>
/// Where an archive kept under a disk watermark stood at its last measurement.
/// </summary>
public enum DiskWatermarkState
{
    /// <summary>Both limits hold and no drain is under way.</summary>
    Within = 0,

    /// <summary>A limit was crossed and the items chosen to reach the target are being deleted.</summary>
    Draining,

    /// <summary>
    /// A limit was crossed and deleting every item old enough to go would still not reach the target:
    /// the archive is too young to evict, or the volume is filled by something the archive does not hold.
    /// </summary>
    Unreachable,

    /// <summary>The archive or the volume could not be measured.</summary>
    MeasurementFailed,
}
