using Bsft.Core;
using Microsoft.Extensions.Configuration;

namespace Bsft.Hosting.StartupValidation;

/// <summary>
/// Startup validator that checks for missing required configuration values such as empty connection strings.
/// </summary>
public sealed class ConfigurationValidator : IStartupValidator
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationValidator"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public ConfigurationValidator(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Validates that required configuration values are present and non-empty.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="StartupValidationResult"/> indicating whether validation passed.</returns>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        var connectionStrings = _configuration.GetSection("ConnectionStrings");
        foreach (var connectionString in connectionStrings.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(connectionString.Value))
            {
                errors.Add($"Connection string '{connectionString.Key}' is empty or missing.");
            }
        }

        if (errors.Count > 0)
            return Task.FromResult(StartupValidationResult.Failure(errors.ToArray()));

        var connCount = connectionStrings.GetChildren().Count();
        var message = connCount > 0
            ? $"Checked {connCount} connection string(s), all valid."
            : "No connection strings configured.";

        return Task.FromResult(StartupValidationResult.Success(message));
    }
}
