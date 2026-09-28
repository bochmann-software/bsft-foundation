using Bsft.Modules.DependencyInjection;
using Bsft.Modules.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Modules.Tests.DependencyInjection;

public class ModulesServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void AddBsftModules_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftModules(EmptyConfig(), _ => { });

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddBsftModules_NullConfig_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftModules(null!, _ => { });

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftModules_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftModules(EmptyConfig(), null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void AddBsftModules_WithModule_RegistersCatalogAndHost()
    {
        var services = new ServiceCollection();

        services.AddBsftModules(EmptyConfig(), cfg => cfg.Add<TestModuleA>());

        var provider = services.BuildServiceProvider();
        var catalog = provider.GetService<IModuleCatalog>();
        var host = provider.GetService<IModuleHost>();

        catalog.Should().NotBeNull();
        host.Should().NotBeNull();
        catalog!.ListModules().Should().ContainSingle(m => m.Name == "TestModuleA");
    }

    [Fact]
    public void AddBsftModules_WithNoModules_RegistersEmptyCatalog()
    {
        var services = new ServiceCollection();

        services.AddBsftModules(EmptyConfig(), _ => { });

        var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IModuleCatalog>();

        catalog.ListModules().Should().BeEmpty();
    }

    [Fact]
    public void AddBsftModules_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddBsftModules(EmptyConfig(), _ => { });

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddBsftModules_WithMultipleModules_LoadsInDependencyOrder()
    {
        var services = new ServiceCollection();

        services.AddBsftModules(EmptyConfig(), cfg =>
        {
            cfg.Add<TestModuleA>();
            cfg.Add<TestModuleB>();
        });

        var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IModuleHost>();

        // TestModuleB depends on TestModuleA, so A should come first
        var moduleNames = host.Modules.Select(m => m.Name).ToList();
        moduleNames.IndexOf("TestModuleA").Should().BeLessThan(moduleNames.IndexOf("TestModuleB"));
    }

    [Fact]
    public void AddBsftModules_WithFromDirectory_RegistersCatalog()
    {
        // Exercise ModulesConfiguration.FromDirectory (lines 28-31):
        // cfg.FromDirectory(path) is invoked, which calls catalog.FromDirectory(path).
        // We point it at a directory containing only Bsft.Modules.dll, which has no IModule
        // implementations, so no modules are loaded but the method is exercised.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Copy only the Bsft.Modules.dll - it has no concrete IModule implementations.
            var modulesDll = typeof(IModule).Assembly.Location;
            File.Copy(modulesDll, Path.Combine(tempDir, Path.GetFileName(modulesDll)));

            var services = new ServiceCollection();
            services.AddBsftModules(EmptyConfig(), cfg => cfg.FromDirectory(tempDir));

            var provider = services.BuildServiceProvider();
            var catalog = provider.GetRequiredService<IModuleCatalog>();

            // No modules from Bsft.Modules.dll (it defines IModule but doesn't implement it)
            catalog.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AddBsftModules_WithFromConfigFile_AndAssemblyPath_LoadsModuleFromDll()
    {
        // Use FromConfigurationFile with an AssemblyPath pointing to the test assembly.
        // This exercises ModuleLoader.ResolveModuleInstances lines 141-149:
        // the path where ModuleType is null and AssemblyPath is used to load the assembly.
        var testAssemblyPath = typeof(TestModuleA).Assembly.Location;
        var tempFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempFile, $$"""
                {
                    "modules": [
                        { "name": "TestModuleA", "assembly": "{{testAssemblyPath.Replace("\\", "\\\\")}}", "enabled": true }
                    ]
                }
                """);

            var services = new ServiceCollection();
            services.AddBsftModules(EmptyConfig(), cfg => cfg.FromConfigurationFile(tempFile));

            var provider = services.BuildServiceProvider();
            var host = provider.GetRequiredService<IModuleHost>();

            // TestModuleA loaded from the assembly path
            host.Modules.Should().Contain(m => m.Name == "TestModuleA");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
