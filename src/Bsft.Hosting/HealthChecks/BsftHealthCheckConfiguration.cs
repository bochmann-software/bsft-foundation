namespace Bsft.Hosting.HealthChecks;

/// <summary>
/// Fluent configuration for BSFT health checks, allowing registration of various health check types.
/// </summary>
/// <remarks>
/// <para>
/// This is a registration builder, not an options type, and it has no <c>SectionName</c>: what it
/// collects are the checks to build, which a configuration provider cannot express.
/// </para>
/// <para>
/// It offers the two checks this package can carry out on its own. A database or tenant-resolution
/// probe is not among them: both would need EF Core and <c>Bsft.Data</c>, which this package stays
/// free of on purpose, so a host adds those itself onto the same builder -
/// <c>services.AddHealthChecks().AddDbContextCheck&lt;AppDbContext&gt;(tags: ["ready"])</c> after
/// <c>AddBsftHealthChecks</c>, tagged "ready" so the readiness endpoint picks them up.
/// </para>
/// </remarks>
public sealed class BsftHealthCheckConfiguration
{
    internal List<ExternalServiceRegistration> ExternalServices { get; } = [];

    internal List<DiskStorageRegistration> DiskStorageChecks { get; } = [];

    /// <summary>
    /// Adds a health check that verifies an external service is reachable via HTTP.
    /// </summary>
    /// <param name="name">A descriptive name for the external service.</param>
    /// <param name="healthUrl">The health check URL of the external service.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftHealthCheckConfiguration AddExternalService(string name, string healthUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name must not be null or empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(healthUrl))
            throw new ArgumentException("Health URL must not be null or empty.", nameof(healthUrl));

        ExternalServices.Add(new ExternalServiceRegistration(name, healthUrl));
        return this;
    }

    /// <summary>
    /// Adds a health check that verifies minimum free disk space is available.
    /// </summary>
    /// <param name="minFreeMb">The minimum required free disk space in megabytes.</param>
    /// <returns>The current configuration instance for fluent chaining.</returns>
    public BsftHealthCheckConfiguration AddDiskStorage(int minFreeMb)
    {
        if (minFreeMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(minFreeMb), "Minimum free MB must be greater than zero.");

        DiskStorageChecks.Add(new DiskStorageRegistration(minFreeMb));
        return this;
    }
}
