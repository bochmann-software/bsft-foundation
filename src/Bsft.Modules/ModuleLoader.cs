using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules;

/// <summary>
/// Handles assembly loading, dependency resolution via topological sort, and module initialization.
/// Uses Kahn's algorithm for topological sorting and detects circular dependencies.
/// </summary>
public sealed class ModuleLoader
{
    private readonly List<IModule> _loadedModules = [];

    /// <summary>
    /// Gets the modules in dependency order after loading.
    /// </summary>
    public IReadOnlyList<IModule> LoadedModules => _loadedModules.AsReadOnly();

    /// <summary>
    /// Loads modules from the catalog, resolves assemblies, performs topological sort,
    /// and calls <see cref="IModule.ConfigureServices"/> on each module in dependency order.
    /// </summary>
    /// <param name="catalog">The module catalog containing descriptors to load.</param>
    /// <param name="services">The service collection to pass to each module.</param>
    /// <param name="configuration">The application configuration to pass to each module.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="ModuleDependencyException">Thrown when a circular or missing dependency is detected.</exception>
    public void LoadModules(IModuleCatalog catalog, IServiceCollection services, IConfiguration configuration)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var descriptors = catalog.ListModules().Where(d => d.Enabled).ToList();
        var resolved = descriptors.Select(descriptor => (Descriptor: descriptor, Module: Resolve(descriptor))).ToList();
        var sorted = Sort(resolved.Select(entry => (entry.Module, Dependencies(entry.Descriptor, entry.Module))).ToList());

        // Reset before configuring so a failure mid-way leaves no stale module list behind.
        _loadedModules.Clear();

        foreach (var module in sorted)
        {
            module.ConfigureServices(services, configuration);
        }

        _loadedModules.AddRange(sorted);
    }

    /// <summary>
    /// Performs a topological sort of modules using Kahn's algorithm.
    /// </summary>
    /// <param name="modules">The modules to sort.</param>
    /// <returns>A list of modules in dependency order (dependencies first).</returns>
    /// <exception cref="ModuleDependencyException">Thrown on circular or missing dependencies.</exception>
    public static List<IModule> TopologicalSort(IReadOnlyList<IModule> modules)
    {
        if (modules is null) throw new ArgumentNullException(nameof(modules));

        return Sort(modules.Select(module => (module, module.DependsOn)).ToList());
    }

    /// <summary>
    /// The dependencies a module is ordered by: the ones it declares itself, together with any the
    /// manifest that named it declares on its behalf.
    /// </summary>
    /// <param name="descriptor">The catalog entry the module came from.</param>
    /// <param name="module">The module itself.</param>
    /// <returns>The names it must be started after.</returns>
    /// <remarks>
    /// A manifest's <c>dependsOn</c> was read into the descriptor and then never looked at, so an
    /// operator declaring an order in <c>modules.json</c> — which the manifest documents as what it is
    /// for — got no order at all. It is taken as an addition rather than a replacement: a deployment
    /// may know of an ordering constraint the module's author could not, and a module's own
    /// declarations are not a deployment's to drop.
    /// </remarks>
    private static IReadOnlyList<string> Dependencies(ModuleDescriptor descriptor, IModule module) =>
        descriptor.DependsOn is null or []
            ? module.DependsOn
            : module.DependsOn.Concat(descriptor.DependsOn).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static List<IModule> Sort(IReadOnlyList<(IModule Module, IReadOnlyList<string> DependsOn)> modules)
    {
        var moduleMap = BuildModuleMap(modules);
        ValidateDependencies(modules, moduleMap);

        var (inDegree, adjacency) = BuildGraph(modules);
        var sorted = RunKahn(moduleMap, inDegree, adjacency);

        if (sorted.Count != modules.Count)
        {
            var cycleMembers = modules.Select(entry => entry.Module)
                .Where(module => !sorted.Contains(module))
                .Select(module => module.Name);

            throw new ModuleDependencyException(
                $"Circular dependency detected among modules: {string.Join(" -> ", cycleMembers)}.");
        }

        return sorted;
    }

    private static Dictionary<string, IModule> BuildModuleMap(
        IReadOnlyList<(IModule Module, IReadOnlyList<string> DependsOn)> modules)
    {
        var map = new Dictionary<string, IModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var (module, _) in modules)
        {
            // Names are matched case-insensitively. A collision here would otherwise silently drop a
            // module and surface later as a misleading "circular dependency", so fail with a clear error.
            if (!map.TryAdd(module.Name, module))
                throw new ModuleDependencyException(
                    $"Duplicate module name '{module.Name}' (module names are case-insensitive).");
        }
        return map;
    }

    private static void ValidateDependencies(
        IReadOnlyList<(IModule Module, IReadOnlyList<string> DependsOn)> modules,
        Dictionary<string, IModule> moduleMap)
    {
        foreach (var (module, dependsOn) in modules)
        {
            foreach (var dep in dependsOn)
            {
                if (!moduleMap.ContainsKey(dep))
                    throw new ModuleDependencyException(
                        $"Module '{module.Name}' depends on '{dep}', which is not registered in the catalog.");
            }
        }
    }

    private static (Dictionary<string, int> InDegree, Dictionary<string, List<string>> Adjacency) BuildGraph(
        IReadOnlyList<(IModule Module, IReadOnlyList<string> DependsOn)> modules)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (module, _) in modules)
        {
            inDegree[module.Name] = 0;
            adjacency[module.Name] = [];
        }

        foreach (var (module, dependsOn) in modules)
        {
            foreach (var dep in dependsOn)
            {
                adjacency[dep].Add(module.Name);
                inDegree[module.Name]++;
            }
        }

        return (inDegree, adjacency);
    }

    private static List<IModule> RunKahn(
        Dictionary<string, IModule> moduleMap,
        Dictionary<string, int> inDegree,
        Dictionary<string, List<string>> adjacency)
    {
        var queue = new Queue<string>();
        foreach (var (name, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(name);
        }

        var sorted = new List<IModule>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(moduleMap[current]);

            foreach (var dependent in adjacency[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        return sorted;
    }

    private static IModule Resolve(ModuleDescriptor descriptor) =>
        ResolveModuleInstance(descriptor)
        ?? throw new ModuleDependencyException(
            $"Could not resolve module type for '{descriptor.Name}'. " +
            "Ensure the assembly path is correct and the module type is accessible.");

    private static IModule? ResolveModuleInstance(ModuleDescriptor descriptor)
    {
        // The catalog built one to read the descriptor from, and a module's constructor is the
        // module's own code: running it a second time for a copy that is thrown away is a side effect
        // nobody asked for.
        if (descriptor.Instance is not null)
            return descriptor.Instance;

        if (descriptor.ModuleType is not null)
        {
            return Activator.CreateInstance(descriptor.ModuleType) as IModule
                ?? throw new ModuleDependencyException(
                    $"Failed to create an instance of module '{descriptor.Name}' from type '{descriptor.ModuleType.FullName}'.");
        }

        return descriptor.AssemblyPath is not null ? ResolveFromAssembly(descriptor) : null;
    }

    private static IModule? ResolveFromAssembly(ModuleDescriptor descriptor)
    {
        // SECURITY: the assembly path is trusted configuration, not user input. Loading an assembly
        // executes its code, so callers must only ever supply paths to assemblies they control.
        Type[] types;

        try
        {
            types = Assembly.LoadFrom(descriptor.AssemblyPath!).GetTypes();
        }
        catch (IOException unloadable)
        {
            throw Unloadable(descriptor, unloadable);
        }
        catch (BadImageFormatException unloadable)
        {
            throw Unloadable(descriptor, unloadable);
        }
        catch (ReflectionTypeLoadException unloadable)
        {
            throw Unloadable(descriptor, unloadable);
        }

        foreach (var type in types)
        {
            if (type is not { IsClass: true, IsAbstract: false } || !typeof(IModule).IsAssignableFrom(type))
                continue;

            // Instantiate each candidate exactly once to read its Name, and keep the matching
            // instance. The previous version created the matching module here and then a second time
            // at the call site, firing constructor side effects twice.
            if (Activator.CreateInstance(type) is IModule module && module.Name == descriptor.Name)
                return module;
        }

        return null;
    }

    /// <summary>Names the failure every unloadable assembly is reported as.</summary>
    /// <param name="descriptor">The module the manifest named.</param>
    /// <param name="unloadable">Whatever the loader raised.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// The package promises that an unloadable assembly is reported as a dependency failure naming
    /// the module. Letting the raw framework exception out breaks that promise for exactly the
    /// entries a manifest exists to name.
    /// </remarks>
    private static ModuleDependencyException Unloadable(ModuleDescriptor descriptor, Exception unloadable)
        => new(
            $"Module '{descriptor.Name}' could not be loaded from '{descriptor.AssemblyPath}'.",
            unloadable);
}
