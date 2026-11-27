namespace Bsft.Retention.Watermarks;

/// <summary>
/// Measures the available space of a local volume through <see cref="DriveInfo"/>.
/// </summary>
/// <remarks>
/// <see cref="DriveInfo.AvailableFreeSpace"/> rather than <see cref="DriveInfo.TotalFreeSpace"/>,
/// because what the archive can still grow into is what this process may write, quotas included. On
/// Windows the volume is the drive of the path's root, so a volume mounted into a folder is measured as
/// the drive that folder sits on, and a UNC path is refused by <see cref="DriveInfo"/>; on Unix the
/// file system the path itself lives on is measured.
/// </remarks>
internal sealed class VolumeSpaceProvider : IVolumeSpaceProvider
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty or white space.</exception>
    public long GetAvailableBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path on the volume to measure is required.", nameof(path));

        return new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace;
    }
}
