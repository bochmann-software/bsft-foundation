namespace Bsft.Hosting.HealthChecks;

/// <summary>
/// Registration data for an external service health check.
/// </summary>
/// <param name="Name">The name of the external service.</param>
/// <param name="HealthUrl">The health endpoint URL.</param>
internal sealed record ExternalServiceRegistration(string Name, string HealthUrl);
