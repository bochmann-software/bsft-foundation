using System.Reflection;
using System.Text.Json;

namespace Bsft.Modules;

/// <summary>
/// Collects module descriptors from multiple sources: explicit registration, JSON configuration files,
/// and directory scanning. Implements <see cref="IModuleCatalog"/> to provide read-only access.
/// </summary>
public sealed class ModuleCatalog : IModuleCatalog
{
    /// <summary>
    /// The version a manifest entry carries until its assembly is loaded and asked for its own. The
    /// manifest names no version, and the discovery endpoint reports the loaded module's instead.
    /// </summary>
    private const string UnresolvedVersion = "0.0.0";

    private readonly List<ModuleDescriptor> _modules = [];

    /// <inheritdoc />
    public IReadOnlyList<ModuleDescriptor> ListModules() => _modules.AsReadOnly();

    /// <summary>Reports whether the catalog already holds a module of that name.</summary>
    /// <param name="name">The technical name to look for.</param>
    /// <returns><see langword="true"/> when a module of that name is registered.</returns>
    /// <remarks>
    /// Names are compared case-insensitively, which is how the loader compares them and how the
    /// documentation describes them. A catalog that let both <c>Orders</c> and <c>orders</c> in would
    /// not fail where the second one was registered but much later, at load time, with a message about
    /// a duplicate name the caller never wrote twice.
    /// </remarks>
    private bool Contains(string name) =>
        _modules.Any(module => string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Registers a module by its CLR type. The module must implement <see cref="IModule"/>
    /// and have a parameterless constructor.
    /// </summary>
    /// <typeparam name="TModule">The type implementing <see cref="IModule"/>.</typeparam>
    /// <returns>This catalog instance for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a module with the same name is already registered.</exception>
    public ModuleCatalog Add<TModule>() where TModule : class, IModule => Add(typeof(TModule));

    /// <summary>
    /// Registers a module by a CLR type known only at run time, such as one a host discovered by scanning
    /// the assemblies it was built with. The type must be a concrete class implementing
    /// <see cref="IModule"/> with a public parameterless constructor, which is what
    /// <see cref="Add{TModule}"/> states as generic constraints.
    /// </summary>
    /// <param name="moduleType">The type implementing <see cref="IModule"/>.</param>
    /// <returns>This catalog instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="moduleType"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="moduleType"/> is not a concrete class implementing <see cref="IModule"/>
    /// with a public parameterless constructor. Refused here, where the caller still knows which type it
    /// passed, rather than as an activation failure at load time.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when a module with the same name is already registered.</exception>
    public ModuleCatalog Add(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        RequireModuleType(moduleType);

        var instance = (IModule)Activator.CreateInstance(moduleType)!;
        var name = instance.Name;

        if (Contains(name))
        {
            throw new InvalidOperationException($"A module with the name '{name}' is already registered.");
        }

        _modules.Add(new ModuleDescriptor(
            Name: name,
            Version: instance.Version,
            Enabled: true,
            ModuleType: moduleType,
            AssemblyPath: null,
            DependsOn: instance.DependsOn.ToArray(),
            Instance: instance));

        return this;
    }

    /// <summary>Refuses a type that cannot be created as a module.</summary>
    /// <param name="moduleType">The type to check.</param>
    /// <exception cref="ArgumentException">Thrown when the type is not a creatable module.</exception>
    internal static void RequireModuleType(Type moduleType)
    {
        if (!IsConcreteClass(moduleType))
            throw new ArgumentException(
                $"The module type '{moduleType.FullName}' is not a concrete class.", nameof(moduleType));

        if (!typeof(IModule).IsAssignableFrom(moduleType))
            throw new ArgumentException(
                $"The module type '{moduleType.FullName}' does not implement {nameof(IModule)}.", nameof(moduleType));

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException(
                $"The module type '{moduleType.FullName}' has no public parameterless constructor.", nameof(moduleType));
    }

    /// <summary>
    /// Says whether a scan may create the type as a module: a concrete, closed class implementing
    /// <see cref="IModule"/> with a public parameterless constructor. A scan skips anything else, such as an
    /// abstract or open generic base module or one built by a factory, rather than failing on it.
    /// </summary>
    /// <param name="type">The scanned type.</param>
    /// <returns><see langword="true"/> when the type can be created as a module.</returns>
    internal static bool IsCreatableModule(Type type) =>
        IsConcreteClass(type) && typeof(IModule).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null;

    private static bool IsConcreteClass(Type type) => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false };

    /// <summary>
    /// Reads module descriptors from a JSON configuration file.
    /// Modules already registered with the same name are skipped.
    /// </summary>
    /// <param name="path">The absolute or relative path to the JSON configuration file.</param>
    /// <returns>This catalog instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the configuration file does not exist.</exception>
    public ModuleCatalog FromConfigurationFile(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Module configuration file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<ModuleConfigurationJson>(json);

        if (config?.Modules is null)
        {
            return this;
        }

        foreach (var entry in config.Modules)
        {
            if (Contains(entry.Name))
            {
                continue;
            }

            _modules.Add(new ModuleDescriptor(
                Name: entry.Name,
                Version: UnresolvedVersion,
                Enabled: entry.Enabled,
                ModuleType: null,
                AssemblyPath: entry.Assembly,
                DependsOn: entry.DependsOn));
        }

        return this;
    }

    /// <summary>
    /// Scans a directory for DLL files containing types that implement <see cref="IModule"/>
    /// and adds them to the catalog. Modules already registered with the same name are skipped.
    /// </summary>
    /// <param name="path">The path to the directory to scan.</param>
    /// <returns>This catalog instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
    /// <exception cref="ModuleDependencyException">
    /// Thrown when a managed assembly in the directory cannot have its types read. A file that is no
    /// managed assembly at all is skipped instead: a plugin directory holds native libraries too.
    /// </exception>
    public ModuleCatalog FromDirectory(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Module directory not found: {path}");
        }

        var dllFiles = Directory.GetFiles(path, "*.dll");

        foreach (var dll in dllFiles)
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var moduleTypes = assembly.GetTypes()
                    .Where(IsCreatableModule);

                foreach (var moduleType in moduleTypes)
                {
                    var instance = (IModule)Activator.CreateInstance(moduleType)!;
                    if (Contains(instance.Name))
                    {
                        continue;
                    }

                    _modules.Add(new ModuleDescriptor(
                        Name: instance.Name,
                        Version: instance.Version,
                        Enabled: true,
                        ModuleType: moduleType,
                        AssemblyPath: dll,
                        DependsOn: instance.DependsOn.ToArray(),
                        Instance: instance));
                }
            }
            catch (ReflectionTypeLoadException unreadable)
            {
                // A managed assembly whose types will not load is a broken deployment, and skipping it
                // would drop whatever modules it carries with nothing said. It was reported to Debug
                // alone before, which a release build compiles away entirely.
                throw new ModuleDependencyException(
                    $"The module assembly '{dll}' could not have its types read.", unreadable);
            }
            catch (BadImageFormatException notAnAssembly)
            {
                // Not a managed assembly. A plugin directory holds native libraries next to the
                // managed ones as a matter of course, so this is the scan reaching a file that was
                // never a candidate, not a failure to report. Discarded rather than left unnamed so
                // that the silence reads as a decision.
                _ = notAnAssembly;
            }
        }

        return this;
    }
}
