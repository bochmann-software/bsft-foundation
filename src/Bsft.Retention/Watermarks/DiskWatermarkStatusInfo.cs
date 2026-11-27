namespace Bsft.Retention.Watermarks;

/// <summary>
/// The last measurement of one archive kept under a disk watermark, and what the pass made of it.
/// </summary>
/// <param name="State">Where the archive stood.</param>
/// <param name="ArchiveBytes">How many bytes the archive held when measured; null when the measurement failed or the archive was not listed because the volume alone decided.</param>
/// <param name="AvailableBytes">
/// How many bytes the volume had available when measured; null when no floor is configured or the
/// measurement failed.
/// </param>
/// <param name="RequiredBytes">How many bytes the pass set out to free to reach the target.</param>
/// <param name="MeasuredAt">When the measurement was taken.</param>
public sealed record DiskWatermarkStatusInfo(
    DiskWatermarkState State,
    long? ArchiveBytes,
    long? AvailableBytes,
    long RequiredBytes,
    DateTimeOffset MeasuredAt);
