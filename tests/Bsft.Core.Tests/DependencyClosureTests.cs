using Bsft.Core.Trees;
using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

/// <summary>
/// Every package in the framework pays for this package's dependency closure, which is why the
/// closure calculator could move here only as a function over the BCL. A reference added for one
/// helper compiles without complaint and lands in the closure of every consumer, so it is asserted.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string Project = "Bsft.Core";

    [Fact]
    public void The_core_restores_no_library_at_all()
        => DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project))
            .Should()
            .BeEmpty();

    [Fact]
    public void The_core_imposes_no_shared_framework_beyond_the_runtime()
        => DependencyClosure.FrameworkReferences(DependencyClosure.RestoredGraph(Project))
            .Should()
            .OnlyContain(name => name == "Microsoft.NETCore.App");

    [Fact]
    public void The_compiled_core_references_nothing_but_the_runtime()
        => typeof(ClosureCalculator).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should()
            .OnlyContain(name => name == "netstandard" || name!.StartsWith("System", StringComparison.Ordinal));
}
