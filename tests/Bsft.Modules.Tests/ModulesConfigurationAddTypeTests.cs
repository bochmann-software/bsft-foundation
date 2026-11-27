using Bsft.Modules.DependencyInjection;
using Bsft.Modules.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Bsft.Modules.Tests;

public class ModulesConfigurationAddTypeTests
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
        var config = new ModulesConfiguration();

        var act = () => config.Add(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("moduleType");
    }

    [Fact]
    public void Add_WithValidModule_ReturnsThisForChaining()
    {
        var config = new ModulesConfiguration();

        var result = config.Add(typeof(ValidModule));

        result.Should().BeSameAs(config);
    }

    [Fact]
    public void Add_WithValidModule_ValidatesEagerly()
    {
        // Unlike the generic Add<T>(), the non-generic Add(Type) validates immediately
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(ValidModule));

        act.Should().NotThrow();
    }

    [Fact]
    public void Add_WithAbstractClass_ThrowsImmediately()
    {
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(AbstractModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithInterface_ThrowsImmediately()
    {
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(IFakeModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithOpenGenericType_ThrowsImmediately()
    {
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(GenericModule<>));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithTypeNotImplementingIModule_ThrowsImmediately()
    {
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(NotAModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*does not implement IModule*");
    }

    [Fact]
    public void Add_WithTypeWithoutParameterlessCtor_ThrowsImmediately()
    {
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(ModuleWithoutParameterlessCtor));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*no public parameterless constructor*");
    }

    [Fact]
    public void Add_WithStructType_ThrowsImmediately()
    {
        var config = new ModulesConfiguration();

        var act = () => config.Add(typeof(StructModule));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("moduleType")
            .WithMessage("*not a concrete class*");
    }

    [Fact]
    public void Add_WithValidTypes_RegistersThemInTheBuiltCatalog()
    {
        var catalog = BuildCatalog(cfg => cfg.Add(typeof(TestModuleA)).Add<TestModuleB>());

        catalog.ListModules().Select(m => m.Name).Should().BeEquivalentTo(["TestModuleA", "TestModuleB"]);
    }

    [Fact]
    public void Add_WithTheSameTypeTwice_FailsWhenTheCatalogIsBuilt()
    {
        // The configuration only checks the type; a duplicate name is the catalog's to refuse.
        var act = () => BuildCatalog(cfg => cfg.Add(typeof(TestModuleA)).Add(typeof(TestModuleA)));

        act.Should().Throw<InvalidOperationException>();
    }

    private static IModuleCatalog BuildCatalog(Action<ModulesConfiguration> configure)
    {
        var services = new ServiceCollection();
        services.AddBsftModules(new ConfigurationBuilder().Build(), configure);

        return services.BuildServiceProvider().GetRequiredService<IModuleCatalog>();
    }
}
