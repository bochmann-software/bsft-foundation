namespace Bsft.Modules;

/// <summary>
/// Describes a module entry in the catalog, including its registration source and dependency information.
/// </summary>
/// <param name="Name">The unique technical name of the module.</param>
/// <param name="Version">The semantic version of the module.</param>
/// <param name="Enabled">Whether the module is enabled and should be loaded.</param>
/// <param name="ModuleType">The CLR type implementing <see cref="IModule"/>, if resolved.</param>
/// <param name="AssemblyPath">The file path to the assembly containing the module, if loaded from disk.</param>
/// <param name="DependsOn">The names of modules this module depends on.</param>
/// <param name="Instance">
/// The module the catalog already built to read this descriptor from, where it built one. Creating a
/// second instance would run the module's constructor twice, once for a copy that is thrown away.
/// </param>
public sealed record ModuleDescriptor(
    string Name,
    string Version,
    bool Enabled,
    Type? ModuleType,
    string? AssemblyPath,
    string[]? DependsOn,
    IModule? Instance = null);
