using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Http.Egress.Tests;

/// <summary>
/// The dependency edges <c>Bsft.Http.Egress</c> must not have. None of them fails as a compiler error:
/// a <c>FrameworkReference</c> to <c>Microsoft.AspNetCore.App</c> here compiles perfectly and quietly
/// makes the package unreferenceable from the dependency-light client it exists for, and an edge back
/// to a consumer would make the shared seam a cycle.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string Project = "Bsft.Http.Egress";

    [Fact]
    public void The_package_does_not_reference_asp_net_core() =>
        Dependencies().Should().NotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));

    [Theory]
    [InlineData("Bsft.Webhooks")]
    [InlineData("Bsft.Connectors")]
    [InlineData("Bsft.Http")]
    public void The_package_does_not_reference_its_consumers(string consumer) =>
        // The references run the other way. An edge back would be a cycle in intent even where the
        // build tolerates it, and would drag a consumer's stack into every other consumer.
        Dependencies().Should().NotContain(consumer);

    [Fact]
    public void The_guard_lives_in_this_assembly() =>
        typeof(OutboundConnectGuard).Assembly.GetName().Name.Should().Be(Project);

    private static IEnumerable<string> Dependencies() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project));
}
