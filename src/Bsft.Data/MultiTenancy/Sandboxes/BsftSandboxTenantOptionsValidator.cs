using Microsoft.Extensions.Options;

namespace Bsft.Data.MultiTenancy.Sandboxes;

/// <summary>Refuses sandbox settings that would produce an identifier nobody can work with.</summary>
internal sealed class BsftSandboxTenantOptionsValidator : IValidateOptions<BsftSandboxTenantOptions>
{
    /// <summary>Validates one named instance of the options.</summary>
    /// <param name="name">The options name, unused: this package registers only the default instance.</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>Success, or a failure naming every setting that cannot be used.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftSandboxTenantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.IdPrefix))
            errors.Add($"{nameof(options.IdPrefix)} must not be blank.");

        if (options.DefaultLifetime <= TimeSpan.Zero)
            errors.Add($"{nameof(options.DefaultLifetime)} must be greater than zero.");

        if (options.MaxProvisionAttempts <= 0)
            errors.Add($"{nameof(options.MaxProvisionAttempts)} must be greater than zero.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
