using Bsft.Caching.Mediator;
using Bsft.Mediator.Transactions;
using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Tests;

public class DefaultBehaviorCatalogTests
{
    [Fact]
    public void Available_WithAnUnreferencedPackage_ContributesNothing()
    {
        // What a host that does not reference a behavior package sees: the slot resolves to no type
        // and drops out, and no host writes a conditional for it.
        var catalog = new DefaultBehaviorCatalog(
        [
            new(MediatorDefaults.Tracing, "Bsft.Mediator.TracingBehavior`2, Bsft.Mediator", "AddBsftMediator(...)"),
            new("Absent", "Bsft.Absent.AbsentBehavior`2, Bsft.Absent", "AddBsftAbsent()")
        ]);

        var available = catalog.Available([]);

        available.Should().ContainSingle();
        available[0].Value.Should().Be(typeof(TracingBehavior<,>));
    }

    [Fact]
    public void Available_WithASuppressedName_LeavesTheSlotOut()
    {
        var catalog = new DefaultBehaviorCatalog();

        var available = catalog.Available([MediatorDefaults.Caching]);

        available.Should().NotContain(slot => slot.Value == typeof(CachingBehavior<,>));
    }

    [Fact]
    public void Available_MatchesSuppressionCaseInsensitively()
    {
        var catalog = new DefaultBehaviorCatalog();

        catalog.Available(["caching"]).Should().NotContain(slot => slot.Value == typeof(CachingBehavior<,>));
    }

    [Fact]
    public void Available_WithNullSuppressions_Throws()
    {
        var catalog = new DefaultBehaviorCatalog();

        var act = () => catalog.Available(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("suppressedNames");
    }

    [Fact]
    public void Constructor_WithNullDescriptors_Throws()
    {
        var act = () => new DefaultBehaviorCatalog(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("descriptors");
    }

    [Fact]
    public void IsKnown_AnswersForTheCanonicalNames()
    {
        var catalog = new DefaultBehaviorCatalog();

        catalog.IsKnown(MediatorDefaults.Transaction).Should().BeTrue();
        catalog.IsKnown("Transactions").Should().BeFalse();
    }

    [Fact]
    public void DescribeKnownNames_ListsEverySlotInCanonicalOrder()
    {
        var catalog = new DefaultBehaviorCatalog();

        catalog.DescribeKnownNames().Should().Be(
            "Tracing, Authorization, OrganizationAuthorization, Validation, Audit, Retry, Transaction, Caching");
    }

    [Fact]
    public void FindSlot_AnswersForTheCanonicalNamesCaseInsensitively()
    {
        var catalog = new DefaultBehaviorCatalog();

        catalog.FindSlot("authorization")!.RegistrationCall.Should().Be("AddBsftAuthMediator()");
        catalog.FindSlot("Authorizations").Should().BeNull();
    }

    [Fact]
    public void ResolveBehaviorTypeName_OnAnAlreadyFoundSlot_SkipsTheSecondLookup()
    {
        // A caller holding the slot resolves its type from the name it already has, rather than
        // searching the catalog a second time.
        var catalog = new DefaultBehaviorCatalog();
        var descriptor = catalog.FindSlot(MediatorDefaults.Tracing)!;

        catalog.ResolveBehaviorTypeName(descriptor.TypeName).Should().Be(typeof(TracingBehavior<,>));
        catalog.ResolveBehaviorTypeName("Bsft.Absent.AbsentBehavior`2, Bsft.Absent").Should().BeNull();
    }

    [Fact]
    public void ResolveRanks_CountsFromTheOutermostSlot()
    {
        var catalog = new DefaultBehaviorCatalog();

        var ranks = catalog.ResolveRanks();

        ranks[typeof(TracingBehavior<,>)].Should().Be(0);
        ranks[typeof(CachingBehavior<,>)].Should().BeGreaterThan(ranks[typeof(TransactionBehavior<,>)]);
    }
}
