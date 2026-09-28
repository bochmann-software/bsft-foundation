namespace Bsft.Modules;

/// <summary>
/// Provides read-only access to the collection of discovered module descriptors.
/// </summary>
public interface IModuleCatalog
{
    /// <summary>
    /// Gets all module descriptors registered in the catalog.
    /// </summary>
    /// <returns>A read-only list of module descriptors.</returns>
    IReadOnlyList<ModuleDescriptor> ListModules();
}
