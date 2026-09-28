namespace Bsft.Core;

/// <summary>
/// Defines a validator that runs at application startup to verify the application is correctly configured.
/// </summary>
public interface IStartupValidator
{
    /// <summary>
    /// Performs startup validation asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="StartupValidationResult"/> indicating whether validation passed.</returns>
    Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}
