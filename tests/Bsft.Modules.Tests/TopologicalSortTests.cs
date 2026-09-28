using Bsft.Modules.Tests.Fakes;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.Modules.Tests;

public class TopologicalSortTests
{
    [Fact]
    public void LinearDependencies_SortsInCorrectOrder()
    {
        var modules = new IModule[] { new TestModuleB(), new TestModuleA() };

        var sorted = ModuleLoader.TopologicalSort(modules);

        sorted.Should().HaveCount(2);
        sorted[0].Name.Should().Be("TestModuleA");
        sorted[1].Name.Should().Be("TestModuleB");
    }

    [Fact]
    public void DiamondDependencies_SortsAllModulesBeforeDependents()
    {
        // C depends on both A and B; B depends on A
        var modules = new IModule[] { new TestModuleC(), new TestModuleB(), new TestModuleA() };

        var sorted = ModuleLoader.TopologicalSort(modules);

        sorted.Should().HaveCount(3);
        var indexA = sorted.FindIndex(m => m.Name == "TestModuleA");
        var indexB = sorted.FindIndex(m => m.Name == "TestModuleB");
        var indexC = sorted.FindIndex(m => m.Name == "TestModuleC");

        indexA.Should().BeLessThan(indexB, "A must come before B");
        indexA.Should().BeLessThan(indexC, "A must come before C");
        indexB.Should().BeLessThan(indexC, "B must come before C");
    }

    [Fact]
    public void CyclicDependency_ThrowsModuleDependencyException()
    {
        var modules = new IModule[] { new CyclicModuleA(), new CyclicModuleB(), new CyclicModuleC() };

        var act = () => ModuleLoader.TopologicalSort(modules);

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void MissingDependency_ThrowsModuleDependencyException()
    {
        var modules = new IModule[] { new MissingDepModule() };

        var act = () => ModuleLoader.TopologicalSort(modules);

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*'NonExistent'*not registered*");
    }

    [Fact]
    public void NoDependencies_ReturnsAllModules()
    {
        var modules = new IModule[] { new TestModuleA() };

        var sorted = ModuleLoader.TopologicalSort(modules);

        sorted.Should().HaveCount(1);
        sorted[0].Name.Should().Be("TestModuleA");
    }

    [Fact]
    public void EmptyList_ReturnsEmptyList()
    {
        var sorted = ModuleLoader.TopologicalSort([]);

        sorted.Should().BeEmpty();
    }

    [Fact]
    public void CaseInsensitiveDuplicateNames_ThrowsClearError()
    {
        // Names differing only by case must fail with a descriptive duplicate-name error rather than
        // silently dropping one module and surfacing as a misleading circular-dependency error.
        // Substitutes are used (not concrete types) so the duplicate stubs do not pollute the
        // assembly scan that other module-discovery tests rely on.
        var first = Substitute.For<IModule>();
        first.Name.Returns("Orders");
        first.DependsOn.Returns([]);
        var second = Substitute.For<IModule>();
        second.Name.Returns("orders");
        second.DependsOn.Returns([]);

        var act = () => ModuleLoader.TopologicalSort([first, second]);

        act.Should().Throw<ModuleDependencyException>()
            .WithMessage("*Duplicate module name 'orders'*");
    }
}
