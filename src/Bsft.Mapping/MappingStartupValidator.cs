using Bsft.Core;

namespace Bsft.Mapping;

/// <summary>
/// Startup validator that verifies all registered mapping profiles are complete by delegating
/// to <see cref="IMappingValidationService"/>.
/// </summary>
public sealed class MappingStartupValidator : IStartupValidator
{
    private readonly IMappingValidationService _validationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MappingStartupValidator"/> class.
    /// </summary>
    /// <param name="validationService">The mapping validation service to delegate to.</param>
    public MappingStartupValidator(IMappingValidationService validationService)
    {
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
    }

    /// <summary>
    /// Validates all registered mapping profiles and reports errors for any unmapped destination properties.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="StartupValidationResult"/> indicating whether validation passed.</returns>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profileCount = _validationService.GetProfileCount();
            _validationService.Validate();
            var message = $"Validated {profileCount} mapping profile(s), all properties covered.";
            return Task.FromResult(StartupValidationResult.Success(message));
        }
        catch (MappingValidationException ex)
        {
            return Task.FromResult(StartupValidationResult.Failure(ex.Errors.ToArray()));
        }
    }
}
