namespace Bsft.Retention.Watermarks;

/// <summary>
/// One item of an archive kept under a disk watermark, as its <see cref="IDiskWatermarkSource"/>
/// lists it.
/// </summary>
/// <param name="Key">What identifies the item to the source's own <see cref="IDiskWatermarkSource.DeleteAsync"/>.</param>
/// <param name="Bytes">How many bytes the item occupies. Must not be negative.</param>
/// <param name="CreatedAt">
/// When the item was created. An item created at or after the sweep's cutoff is never deleted.
/// </param>
public sealed record DiskWatermarkItemInfo(string Key, long Bytes, DateTimeOffset CreatedAt);
