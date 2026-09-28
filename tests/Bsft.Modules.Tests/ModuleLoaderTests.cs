using Bsft.Modules.Tests.Fakes;
using Bsft.Modules.Tests.Support;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModuleLoaderTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void LoadModules_NullCatalog_ThrowsArgumentNullException()
    {
        var loader = new ModuleLoader();

        var act = () => loader.LoadModules(null!, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }

    [Fact]
    public void LoadModules_NullServices_ThrowsArgumentNullException()
    {
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();
        catalog.ListModules().Returns(new List<ModuleDescriptor>());

        var act = () => loader.LoadModules(catalog, null!, EmptyConfig());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void LoadModules_NullConfig_ThrowsArgumentNullException()
    {
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();
        catalog.ListModules().Returns(new List<ModuleDescriptor>());

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void LoadModules_WithDescriptorHavingNullModuleTypeAndNullAssemblyPath_ThrowsModuleDependencyException()
    {
        // Exercises ModuleLoader.ResolveModuleInstances lines 151-155:
        // moduleType is null AND AssemblyPath is null => block is skipped => throws.
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        var descriptor = new ModuleDescriptor(
            Name: "GhostModule",
            Version: "1.0.0",
            Enabled: true,
            ModuleType: null,        // null => will try AssemblyPath
            AssemblyPath: null,      // null => block skipped => moduleType stays null
            DependsOn: []);

        catalog.ListModules().Returns(new List<ModuleDescriptor> { descriptor });

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*Could not resolve module type for 'GhostModule'*");
    }

    [Fact]
    public void LoadModules_WithAssemblyPathButModuleNameNotFound_ThrowsModuleDependencyException()
    {
        // Exercises ModuleLoader.ResolveModuleInstances lines 151-155 via the assembly-scanning path:
        // AssemblyPath points to a valid assembly but no IModule with the given name exists in it.
        // The FirstOrDefault returns null => moduleType stays null => throws.
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        // Use a real assembly that exists but name that doesn't match any IModule in it.
        var testAssemblyPath = typeof(TestModuleA).Assembly.Location;

        var descriptor = new ModuleDescriptor(
            Name: "NoSuchModuleInAssembly",
            Version: "1.0.0",
            Enabled: true,
            ModuleType: null,
            AssemblyPath: testAssemblyPath,
            DependsOn: []);

        catalog.ListModules().Returns(new List<ModuleDescriptor> { descriptor });

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*Could not resolve module type for 'NoSuchModuleInAssembly'*");
    }

    [Fact]
    public void LoadModules_WithDisabledModule_SkipsIt()
    {
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        var descriptor = new ModuleDescriptor(
            Name: "DisabledModule",
            Version: "1.0.0",
            Enabled: false,
            ModuleType: typeof(TestModuleA),
            AssemblyPath: null,
            DependsOn: []);

        catalog.ListModules().Returns(new List<ModuleDescriptor> { descriptor });

        loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        loader.LoadedModules.Should().BeEmpty();
    }

    [Fact]
    public void LoadModules_WithEnabledModule_LoadsIt()
    {
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        var descriptor = new ModuleDescriptor(
            Name: "TestModuleA",
            Version: "1.0.0",
            Enabled: true,
            ModuleType: typeof(TestModuleA),
            AssemblyPath: null,
            DependsOn: []);

        catalog.ListModules().Returns(new List<ModuleDescriptor> { descriptor });

        loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        loader.LoadedModules.Should().HaveCount(1);
        loader.LoadedModules[0].Name.Should().Be("TestModuleA");
    }

    [Fact]
    public void LoadModules_WithNonIModuleType_ThrowsModuleDependencyException()
    {
        // Exercises ModuleLoader.ResolveModuleInstances lines 159-161:
        // Activator.CreateInstance(moduleType) is not IModule instance => throws.
        // Passing typeof(object) as ModuleType: Activator.CreateInstance(typeof(object)) returns a new object(),
        // which is not an IModule, triggering the exception branch.
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        var descriptor = new ModuleDescriptor(
            Name: "FakeModule",
            Version: "1.0.0",
            Enabled: true,
            ModuleType: typeof(object),  // object is not IModule
            AssemblyPath: null,
            DependsOn: []);

        catalog.ListModules().Returns(new List<ModuleDescriptor> { descriptor });

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*Failed to create an instance of module 'FakeModule'*");
    }

    /// <summary>
    /// The manifest's own <c>dependsOn</c> orders the modules too. <c>TestModuleB</c> already declares
    /// <c>TestModuleA</c>, so a manifest declaring the reverse closes a cycle: that the load fails is
    /// what shows the manifest was read rather than discarded.
    /// </summary>
    [Fact]
    public void LoadModules_WithADependencyOnlyTheManifestDeclares_OrdersByItToo()
    {
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        catalog.ListModules().Returns(new List<ModuleDescriptor>
        {
            new("TestModuleA", "1.0.0", Enabled: true, ModuleType: typeof(TestModuleA), AssemblyPath: null,
                DependsOn: ["TestModuleB"]),
            new("TestModuleB", "1.1.0", Enabled: true, ModuleType: typeof(TestModuleB), AssemblyPath: null,
                DependsOn: [])
        });

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*Circular dependency*");
    }

    /// <summary>An assembly the manifest names but the deployment does not carry names the module.</summary>
    [Fact]
    public void LoadModules_WithAnAssemblyThatCannotBeLoaded_NamesTheModule()
    {
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();

        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");

        var descriptor = new ModuleDescriptor(
            Name: "Orders",
            Version: "1.0.0",
            Enabled: true,
            ModuleType: null,
            AssemblyPath: missing,
            DependsOn: []);

        catalog.ListModules().Returns(new List<ModuleDescriptor> { descriptor });

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage($"*'Orders'*{Path.GetFileName(missing)}*")
            .WithInnerException<FileNotFoundException>();
    }

    [Fact]
    public void LoadModules_WithAnAssemblyPathThatIsNotAManagedAssembly_NamesTheModule()
    {
        // A file that looks like a DLL but is not one: the loader has to say which module it was
        // configured for, because the path came from that module's own entry.
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03]);

        try
        {
            catalog.ListModules().Returns(new List<ModuleDescriptor>
            {
                new(
                    Name: "Orders",
                    Version: "1.0.0",
                    Enabled: true,
                    ModuleType: null,
                    AssemblyPath: path,
                    DependsOn: []),
            });

            var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

            act.Should().Throw<ModuleDependencyException>()
                .WithMessage($"*'Orders'*{Path.GetFileName(path)}*")
                .WithInnerException<BadImageFormatException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadModules_WithAnAssemblyWhoseTypesCannotBeRead_NamesTheModule()
    {
        // The assembly loads but references one that is not deployed, so reading its types fails.
        // That is a broken deployment of a configured module rather than a stray file, and the
        // refusal has to carry both the module's name and what went wrong underneath.
        var loader = new ModuleLoader();
        var catalog = Substitute.For<IModuleCatalog>();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BrokenAssemblyBytes.GetBytes());

        catalog.ListModules().Returns(new List<ModuleDescriptor>
        {
            new(
                Name: "Orders",
                Version: "1.0.0",
                Enabled: true,
                ModuleType: null,
                AssemblyPath: path,
                DependsOn: []),
        });

        var act = () => loader.LoadModules(catalog, new ServiceCollection(), EmptyConfig());

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage($"*'Orders'*{Path.GetFileName(path)}*")
            .WithInnerException<ReflectionTypeLoadException>();

        // The file is left where it is: LoadFrom succeeded, so the runtime holds it open for the
        // life of the process and deleting it here would fail the test for the wrong reason.
    }
}
