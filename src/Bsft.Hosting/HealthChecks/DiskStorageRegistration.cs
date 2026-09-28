namespace Bsft.Hosting.HealthChecks;

/// <summary>
/// Registration data for a disk storage health check.
/// </summary>
/// <param name="MinFreeMb">The minimum required free disk space in megabytes.</param>
internal sealed record DiskStorageRegistration(int MinFreeMb);
