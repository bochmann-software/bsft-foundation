using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.StartGates.Tests;

/// <summary>
/// Every loop package takes this one for its registration, so its closure is what all of them pay: the gate
/// contract in <c>Bsft.Core</c> and nothing else from the framework.
/// </summary>
public sealed class DependencyClosureTests
{
    [Fact]
    public void RestoredGraph_TakesOnlyBsftCoreFromBsft() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph("Bsft.Hosting.StartGates"))
            .Where(name => name.StartsWith("Bsft.", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo(["Bsft.Core"]);
}
