using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bsft.Hosting.HealthChecks;

/// <summary>
/// Health check that verifies minimum free disk space is available on the application drive.
/// </summary>
public sealed class DiskStorageHealthCheck : IHealthCheck
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    private readonly int _minFreeMb;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskStorageHealthCheck"/> class.
    /// </summary>
    /// <param name="minFreeMb">The minimum required free disk space in megabytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minFreeMb"/> is not positive.</exception>
    public DiskStorageHealthCheck(int minFreeMb)
    {
        if (minFreeMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(minFreeMb), "Minimum free MB must be greater than zero.");

        _minFreeMb = minFreeMb;
    }

    /// <summary>
    /// Performs the health check by verifying available disk space.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="HealthCheckResult"/> indicating the health status.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var drivePath = Path.GetPathRoot(AppContext.BaseDirectory) ?? "/";
        var driveInfo = new DriveInfo(drivePath);

        var freeSpaceMb = driveInfo.AvailableFreeSpace / BytesPerMegabyte;

        var result = freeSpaceMb >= _minFreeMb
            ? HealthCheckResult.Healthy($"Disk has {freeSpaceMb} MB free (minimum: {_minFreeMb} MB).")
            : HealthCheckResult.Unhealthy($"Disk has only {freeSpaceMb} MB free, which is below the minimum of {_minFreeMb} MB.");

        return Task.FromResult(result);
    }
}
