namespace Bsft.Mapping;

/// <summary>
/// Base class for defining object mapping configurations. Derived classes override
/// <see cref="Configure"/> to register type-pair mappings using the provided <see cref="ProfileBuilder"/>.
/// </summary>
/// <remarks>
/// This type is designed for inheritance and is the one extension point of the package: an
/// application declares a profile per bounded set of mappings, and <c>AddBsftMapping</c> discovers
/// every non-abstract type deriving from it in the scanned assemblies. It needs a public
/// parameterless constructor, because it is instantiated by reflection.
/// </remarks>
public abstract class MappingProfileBase
{
    /// <summary>
    /// Configures the mapping definitions for this profile.
    /// </summary>
    /// <param name="builder">The profile builder used to register mappings.</param>
    public abstract void Configure(ProfileBuilder builder);
}
