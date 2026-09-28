namespace Bsft.Mapping;

/// <summary>
/// Validates that all registered mapping configurations are complete.
/// </summary>
public interface IMappingValidationService
{
    /// <summary>
    /// Returns the number of registered type-pair mappings.
    /// </summary>
    int GetProfileCount();

    /// <summary>
    /// Validates all registered mappings and throws a <see cref="Bsft.Mapping.MappingValidationException"/>
    /// if any destination properties are unmapped, not explicitly configured, or not ignored.
    /// </summary>
    /// <exception cref="Bsft.Mapping.MappingValidationException">
    /// Thrown when one or more destination properties lack a mapping configuration.
    /// </exception>
    void Validate();
}
