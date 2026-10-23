using Bsft.Data.Modeling;
using Bsft.Data.Testing;
using Bsft.Data.Tests.Modeling;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Testing;

/// <summary>
/// <see cref="TestRelationalModelWarmup"/> is the public entry point a test calls instead of
/// <see cref="RelationalModelWarmupHostedService"/>; this asserts it reaches the same build.
/// </summary>
public sealed class TestRelationalModelWarmupTests
{
    [Fact]
    public void WithNullContextThrows()
    {
        var act = () => TestRelationalModelWarmup.EnsureRelationalModelBuilt(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildsTheRuntimeModelOfARelationalContext()
    {
        using var context = new FreshModelContext(Guid.NewGuid());

        context.EnsureRelationalModelBuilt();

        RelationalModelWarmup.IsBuilt(context.Model).Should().BeTrue();
    }

    [Fact]
    public void LeavesANonRelationalContextAlone()
    {
        using var context = new FreshModelContext(Guid.NewGuid(), relational: false);

        var act = () => context.EnsureRelationalModelBuilt();

        act.Should().NotThrow("a provider without a relational model has nothing to build");
    }
}
