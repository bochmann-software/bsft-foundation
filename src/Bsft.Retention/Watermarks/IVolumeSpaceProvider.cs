namespace Bsft.Retention.Watermarks;

/// <summary>
/// Supplies how much space is available on the volume a path lives on.
/// </summary>
/// <remarks>
/// A seam rather than a direct <see cref="DriveInfo"/> call, so a test drives the watermark with a
/// volume of any size (<c>Bsft.Retention.Testing.FakeVolumeSpaceProvider</c>) and a host whose archive
/// lives somewhere <see cref="DriveInfo"/> cannot measure, a quota-limited share for instance,
/// registers its own measurement before <c>AddBsftRetentionDiskWatermark</c>.
/// </remarks>
public interface IVolumeSpaceProvider
{
    /// <summary>
    /// Measures the space available to this process on the volume <paramref name="path"/> lives on.
    /// </summary>
    /// <param name="path">A path on the volume to measure; relative paths resolve against the working directory.</param>
    /// <returns>The available bytes.</returns>
    long GetAvailableBytes(string path);
}
