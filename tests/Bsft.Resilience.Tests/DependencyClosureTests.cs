using Bsft.Resilience.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Resilience.Tests;

/// <summary>
/// The dependency edge this package must not have. It fails as no compiler error: referencing the
/// mediator compiles perfectly and quietly puts the mediator and its dispatch machinery
/// into the closure of every consumer of this package, whether or not it dispatches anything.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string Project = "Bsft.Resilience";

    [Fact]
    public void The_resilience_core_does_not_depend_on_the_mediator()
        => DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project))
            .Should()
            .NotContain(name => name.StartsWith("Bsft.Mediator", StringComparison.Ordinal));

    [Fact]
    public void The_compiled_resilience_core_references_no_mediator_assembly()
        => typeof(ResilienceServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should()
            .NotContain(name => name!.StartsWith("Bsft.Mediator", StringComparison.Ordinal));
}
