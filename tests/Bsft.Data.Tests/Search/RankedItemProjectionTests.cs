using System.Linq.Expressions;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Search;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// The shape matters as much as the result: Entity Framework Core can push an <c>ORDER BY</c> on a
/// member of a member-initialized projection down into SQL, and cannot for a constructor-bound one.
/// </summary>
public class RankedItemProjectionTests
{
    [Fact]
    public void For_BindsTheEntityAndItsScore()
    {
        var parameter = Expression.Parameter(typeof(string), "e");

        var projection = Expression.Lambda<Func<string, RankedItem<string>>>(
            RankedItemProjection.For<string>(parameter, Expression.Constant(2.5d)),
            parameter);

        var hit = projection.Compile()("Alice");

        hit.Item.Should().Be("Alice");
        hit.Score.Should().Be(2.5d);
    }

    [Fact]
    public void For_ProducesAMemberInitialization_NotAConstructorCall()
    {
        var parameter = Expression.Parameter(typeof(string), "e");

        var body = RankedItemProjection.For<string>(parameter, Expression.Constant(1d));

        body.NodeType.Should().Be(ExpressionType.MemberInit);
        body.Bindings.Select(binding => binding.Member.Name)
            .Should().BeEquivalentTo([nameof(RankedItem<string>.Item), nameof(RankedItem<string>.Score)]);
    }

    [Fact]
    public void For_TheSameEntityTypeTwice_BindsTheSameMembers()
    {
        // The properties are resolved once per closed entity type, so the second projection must read
        // the identical PropertyInfo rather than a freshly looked-up equal one.
        var parameter = Expression.Parameter(typeof(string), "e");

        var first = RankedItemProjection.For<string>(parameter, Expression.Constant(1d));
        var second = RankedItemProjection.For<string>(parameter, Expression.Constant(2d));

        first.Bindings[0].Member.Should().BeSameAs(second.Bindings[0].Member);
        first.Bindings[1].Member.Should().BeSameAs(second.Bindings[1].Member);
    }
}
