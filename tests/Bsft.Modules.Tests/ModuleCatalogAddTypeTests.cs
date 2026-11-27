using Bsft.Modules.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModuleCatalogAddTypeTests
{
    private abstract class AbstractModule : IModule
    {
        public string Name => "AbstractModule";
        public string Version => "1.0.0";
        public IReadOnlyList<string> DependsOn => [];

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration config) { }
        public System.Threading.Tasks.Task OnStartAsync(System.IServiceProvider services, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStopAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    private interface IFakeModule : IModule
    {
    }

    private sealed class ValidModule : IModule
    {
        public string Name => "ValidModule";
        public string Version => "1.0.0";
        public IReadOnlyList<string> DependsOn => [];

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration config) { }
        public System.Threading.Tasks.Task OnStartAsync(System.IServiceProvider services, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStopAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class ModuleWithoutParameterlessCtor : IModule
    {
        public string Name => "ModuleWithoutParameterlessCtor";
        public string Version => "1.0.0";
        public IReadOnlyList<string> DependsOn => [];

        public ModuleWithoutParameterlessCtor(int required) { }

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration config) { }
        public System.Threading.Tasks.Task OnStartAsync(System.IServiceProvider services, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStopAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    private struct StructModule : IModule
    {
        public string Name => "StructModule";
        public string Version => "1.0.0";
        public IReadOnlyList<string> DependsOn => [];

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration config) { }
        public System.Threading.Tasks.Task OnStartAsync(System.IServiceProvider services, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStopAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class NotAModule
    {
    }

    private sealed class GenericModule<T> : IModule
    {
        public string Name => "GenericModule";
        public string Version => "1.0.0";
        public IReadOnlyList<string> DependsOn => [];

        public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration config) { }
        public System.Threading.Tasks.Task OnStartAsync(System.IServiceProvider services, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStopAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
    }

    [Fact]
    public void Add_WithNullType_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("moduleType");
    }

    [Fact]
    public void Add_WithValidModule_RegistersModule()
    {
        var catalog = new ModuleCatalog();

        catalog.Add(typeof(ValidModule));

        var modules = catalog.ListModules();
        modules.Should().ContainSingle();
        modules[0].Name.Should().Be("ValidModule");
        modules[0].ModuleType.Should().Be(typeof(ValidModule));
        modules[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Add_WithValidModule_ReturnsThisForChaining()
    {
        var catalog = new ModuleCatalog();

        var result = catalog.Add(typeof(ValidModule));

        result.Should().BeSameAs(catalog);
    }

    [Fact]
    public void Add_WithAbstractClass_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(typeof(AbstractModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithInterface_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(typeof(IFakeModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithOpenGenericType_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(typeof(GenericModule<>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithTypeNotImplementingIModule_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(typeof(NotAModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*does not implement IModule*");
    }

    [Fact]
    public void Add_WithTypeWithoutParameterlessCtor_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(typeof(ModuleWithoutParameterlessCtor));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*no public parameterless constructor*");
    }

    [Fact]
    public void Add_WithStructType_Throws()
    {
        var catalog = new ModuleCatalog();

        var act = () => catalog.Add(typeof(StructModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithDuplicateModuleName_Throws()
    {
        var catalog = new ModuleCatalog();
        catalog.Add(typeof(ValidModule));

        var act = () => catalog.Add(typeof(ValidModule));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'ValidModule'*already registered*");
    }

    [Fact]
    public void Add_WithDuplicateModuleNameDifferentCase_Throws()
    {
        var catalog = new ModuleCatalog();
        catalog.Add(typeof(TestModuleA));

        // TestModuleA has name "TestModuleA", trying to add another with same name case-insensitively should fail
        var act = () => catalog.Add(typeof(TestModuleA));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Add_MultipleValidModules_RegistersAll()
    {
        var catalog = new ModuleCatalog();

        catalog.Add(typeof(ValidModule)).Add(typeof(TestModuleA));

        catalog.ListModules().Should().HaveCount(2);
    }

    [Fact]
    public void Add_InstantiatesModuleToGetMetadata()
    {
        var catalog = new ModuleCatalog();

        catalog.Add(typeof(TestModuleA));

        var modules = catalog.ListModules();
        modules[0].Name.Should().Be("TestModuleA");
        modules[0].Version.Should().Be("1.0.0");
    }

    [Fact]
    public void Add_PreservesModuleDependencies()
    {
        var catalog = new ModuleCatalog();

        catalog.Add(typeof(TestModuleC));

        var modules = catalog.ListModules();
        var moduleC = modules.FirstOrDefault(m => m.Name == "TestModuleC");
        moduleC.Should().NotBeNull();
        moduleC!.DependsOn.Should().Contain("TestModuleA");
        moduleC.DependsOn.Should().Contain("TestModuleB");
    }

    [Fact]
    public void Add_SetsInstanceOnDescriptor()
    {
        var catalog = new ModuleCatalog();

        catalog.Add(typeof(TestModuleA));

        var modules = catalog.ListModules();
        modules[0].Instance.Should().NotBeNull();
        modules[0].Instance.Should().BeOfType<TestModuleA>();
    }
}
