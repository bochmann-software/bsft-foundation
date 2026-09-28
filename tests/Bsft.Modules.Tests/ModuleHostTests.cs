using Bsft.Modules.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModuleHostTests
{
    [Fact]
    public async Task StartAsync_CallsOnStartInDependencyOrder()
    {
        var order = new List<string>();
        var moduleA = new TrackedModule("A", [], order);
        var moduleB = new TrackedModule("B", ["A"], order);
        var modules = new List<IModule> { moduleA, moduleB };
        var catalog = CreateCatalog(modules);
        var host = new ModuleHost(modules, catalog);
        var services = new ServiceCollection().BuildServiceProvider();

        await host.StartAsync(services);

        order.Should().ContainInOrder("A:start", "B:start");
    }

    [Fact]
    public async Task StopAsync_CallsOnStopInReverseDependencyOrder()
    {
        var order = new List<string>();
        var moduleA = new TrackedModule("A", [], order);
        var moduleB = new TrackedModule("B", ["A"], order);
        var modules = new List<IModule> { moduleA, moduleB };
        var catalog = CreateCatalog(modules);
        var host = new ModuleHost(modules, catalog);

        await host.StopAsync();

        order.Should().ContainInOrder("B:stop", "A:stop");
    }

    [Fact]
    public async Task StartAsync_WithThreeModules_MaintainsOrder()
    {
        var order = new List<string>();
        var moduleA = new TrackedModule("A", [], order);
        var moduleB = new TrackedModule("B", ["A"], order);
        var moduleC = new TrackedModule("C", ["A", "B"], order);
        var modules = new List<IModule> { moduleA, moduleB, moduleC };
        var catalog = CreateCatalog(modules);
        var host = new ModuleHost(modules, catalog);
        var services = new ServiceCollection().BuildServiceProvider();

        await host.StartAsync(services);

        order.Should().ContainInOrder("A:start", "B:start", "C:start");
    }

    [Fact]
    public async Task StopAsync_WithThreeModules_ReversesOrder()
    {
        var order = new List<string>();
        var moduleA = new TrackedModule("A", [], order);
        var moduleB = new TrackedModule("B", ["A"], order);
        var moduleC = new TrackedModule("C", ["A", "B"], order);
        var modules = new List<IModule> { moduleA, moduleB, moduleC };
        var catalog = CreateCatalog(modules);
        var host = new ModuleHost(modules, catalog);

        await host.StopAsync();

        order.Should().ContainInOrder("C:stop", "B:stop", "A:stop");
    }

    [Fact]
    public void GetModuleInfo_ReturnsCorrectMetadata()
    {
        var modules = new List<IModule> { new TestModuleA(), new TestModuleB() };
        var catalog = new ModuleCatalog();
        catalog.Add<TestModuleA>();
        catalog.Add<TestModuleB>();
        var host = new ModuleHost(modules, catalog);

        var info = host.ListModuleInfo();

        info.Should().HaveCount(2);
        info[0].Name.Should().Be("TestModuleA");
        info[0].Version.Should().Be("1.0.0");
        info[0].Enabled.Should().BeTrue();
    }

    /// <summary>A manifest carries no version, so the loaded module is the one that knows it.</summary>
    [Fact]
    public void GetModuleInfo_WithAModuleTheManifestNamed_ReportsTheVersionTheModuleCarries()
    {
        var catalog = Substitute.For<IModuleCatalog>();
        catalog.ListModules().Returns(new List<ModuleDescriptor>
        {
            new("TestModuleB", "0.0.0", Enabled: true, ModuleType: null, AssemblyPath: "./b.dll", DependsOn: [])
        });

        var host = new ModuleHost([new TestModuleB()], catalog);

        host.ListModuleInfo()[0].Version.Should().Be("1.1.0");
    }

    /// <summary>A disabled module was never loaded, so the catalog stays the only source there.</summary>
    [Fact]
    public void GetModuleInfo_WithADisabledModule_ReportsWhatTheCatalogRecorded()
    {
        var catalog = Substitute.For<IModuleCatalog>();
        catalog.ListModules().Returns(new List<ModuleDescriptor>
        {
            new("Shipping", "0.0.0", Enabled: false, ModuleType: null, AssemblyPath: "./s.dll", DependsOn: [])
        });

        var host = new ModuleHost([], catalog);

        var info = host.ListModuleInfo()[0];
        info.Version.Should().Be("0.0.0");
        info.Enabled.Should().BeFalse();
    }

    /// <summary>The set of modules cannot change, so the answer is built once.</summary>
    [Fact]
    public void GetModuleInfo_CalledTwice_ReadsTheCatalogOnce()
    {
        var catalog = Substitute.For<IModuleCatalog>();
        catalog.ListModules().Returns(new List<ModuleDescriptor>
        {
            new("TestModuleA", "1.0.0", Enabled: true, ModuleType: typeof(TestModuleA), AssemblyPath: null, DependsOn: [])
        });

        var host = new ModuleHost([new TestModuleA()], catalog);

        host.ListModuleInfo().Should().BeSameAs(host.ListModuleInfo());
        catalog.Received(1).ListModules();
    }

    [Fact]
    public void Catalog_ReturnsCatalogPassedToConstructor()
    {
        var modules = new List<IModule> { new TestModuleA() };
        var catalog = new ModuleCatalog();
        catalog.Add<TestModuleA>();
        var host = new ModuleHost(modules, catalog);

        var result = host.Catalog;

        result.Should().BeSameAs(catalog);
    }

    private static IModuleCatalog CreateCatalog(IReadOnlyList<IModule> modules)
    {
        var catalog = Substitute.For<IModuleCatalog>();
        var descriptors = modules.Select(m =>
            new ModuleDescriptor(m.Name, m.Version, true, m.GetType(), null, m.DependsOn.ToArray())).ToList();
        catalog.ListModules().Returns(descriptors);
        return catalog;
    }
}
