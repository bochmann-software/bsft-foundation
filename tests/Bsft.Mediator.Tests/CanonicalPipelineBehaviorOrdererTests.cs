using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Tests;

public class CanonicalPipelineBehaviorOrdererTests
{
    [Fact]
    public void Order_WithFewerThanTwoKnownBehaviors_LeavesTheSequenceAlone()
    {
        var orderer = new CanonicalPipelineBehaviorOrderer(new DefaultBehaviorCatalog());
        IReadOnlyList<IPipelineBehavior<ICommand<string>, string>> behaviors =
        [
            new OuterProbeBehavior<ICommand<string>, string>(),
            new InnerProbeBehavior<ICommand<string>, string>()
        ];

        orderer.Order(behaviors).Should().BeSameAs(behaviors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Order_WithExactlyTwoKnownBehaviors_PutsThemInCatalogOrderAndLeavesTheUnknownInPlace(bool registeredInOrder)
    {
        // Two known behaviors are the smallest set there is anything to sort in; an unknown one
        // between them keeps its slot whichever way round the two arrive.
        var orderer = new CanonicalPipelineBehaviorOrderer(new DefaultBehaviorCatalog(
        [
            new("Outer", "Bsft.Mediator.Tests.OuterProbeBehavior`2, Bsft.Mediator.Tests", "AddOuter()"),
            new("Inner", "Bsft.Mediator.Tests.InnerProbeBehavior`2, Bsft.Mediator.Tests", "AddInner()")
        ]));
        IPipelineBehavior<ICommand<string>, string> outer = new OuterProbeBehavior<ICommand<string>, string>();
        IPipelineBehavior<ICommand<string>, string> inner = new InnerProbeBehavior<ICommand<string>, string>();
        IPipelineBehavior<ICommand<string>, string> unknown = new ConcreteProbeBehavior();
        IReadOnlyList<IPipelineBehavior<ICommand<string>, string>> behaviors =
            registeredInOrder ? [outer, unknown, inner] : [inner, unknown, outer];

        var ordered = orderer.Order(behaviors);

        ordered.Should().Equal([outer, unknown, inner], (left, right) => ReferenceEquals(left, right));
    }

    [Fact]
    public void Order_WithANonGenericBehavior_TreatsItAsUnknown()
    {
        // A behavior that is not an open generic closed by the container cannot be a catalog slot,
        // and keeping its position is what leaves a host behavior where it was registered.
        var orderer = new CanonicalPipelineBehaviorOrderer(new DefaultBehaviorCatalog());
        IReadOnlyList<IPipelineBehavior<ICommand<string>, string>> behaviors = [new ConcreteProbeBehavior()];

        orderer.Order(behaviors).Should().BeSameAs(behaviors);
    }

    [Fact]
    public void Order_WithNullBehaviors_Throws()
    {
        var orderer = new CanonicalPipelineBehaviorOrderer(new DefaultBehaviorCatalog());

        var act = () => orderer.Order<object>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("behaviors");
    }

    [Fact]
    public void Constructor_WithNullCatalog_Throws()
    {
        var act = () => new CanonicalPipelineBehaviorOrderer(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }
}
