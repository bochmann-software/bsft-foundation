using Bsft.Core.Trees;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Trees;

public sealed class ClosureCalculatorTests
{
    private readonly ClosureCalculator _sut = new();

    // ── helpers ──────────────────────────────────────────────────────────────

    // The row the moved organization tests asserted on, now built through the factory like every
    // consumer builds its own entity.
    private sealed record ClosureRow(Guid AncestorId, Guid DescendantId, int Depth);

    private IReadOnlyCollection<ClosureRow> Compute(IEnumerable<(Guid Id, Guid? ParentId)> nodes)
        => _sut.Compute(nodes, (ancestorId, descendantId, depth) => new ClosureRow(ancestorId, descendantId, depth));

    private static ClosureRow? Row(
        IReadOnlyCollection<ClosureRow> rows,
        Guid ancestor,
        Guid descendant)
        => rows.FirstOrDefault(r => r.AncestorId == ancestor && r.DescendantId == descendant);

    // ── single chain ─────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenSingleChainCompanyTeamSubTeam_YieldsCorrectDepths()
    {
        var company = Guid.NewGuid();
        var team = Guid.NewGuid();
        var subTeam = Guid.NewGuid();

        var nodes = new (Guid Id, Guid? ParentId)[]
        {
            (company, null),
            (team, company),
            (subTeam, team),
        };

        var result = Compute(nodes);

        // Self rows
        Row(result, company, company)!.Depth.Should().Be(0);
        Row(result, team, team)!.Depth.Should().Be(0);
        Row(result, subTeam, subTeam)!.Depth.Should().Be(0);

        // team → company
        Row(result, company, team)!.Depth.Should().Be(1);

        // subTeam → team
        Row(result, team, subTeam)!.Depth.Should().Be(1);

        // subTeam → company (transitive)
        Row(result, company, subTeam)!.Depth.Should().Be(2);

        // total rows: company(1) + team(2) + subTeam(3)
        result.Should().HaveCount(6);
    }

    // ── self row always present ───────────────────────────────────────────────

    [Fact]
    public void Compute_WhenSingleNode_YieldsSelfRowAtDepthZero()
    {
        var id = Guid.NewGuid();
        var result = Compute(new (Guid, Guid?)[] { (id, null) });

        result.Should().HaveCount(1);
        var row = result.Single();
        row.AncestorId.Should().Be(id);
        row.DescendantId.Should().Be(id);
        row.Depth.Should().Be(0);
    }

    // ── multiple roots ────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenMultipleRoots_EachRootHasOnlySelfRow()
    {
        var root1 = Guid.NewGuid();
        var root2 = Guid.NewGuid();
        var child1 = Guid.NewGuid();

        var nodes = new (Guid Id, Guid? ParentId)[]
        {
            (root1, null),
            (root2, null),
            (child1, root1),
        };

        var result = Compute(nodes);

        // root2 must have only its self row — no cross-contamination from root1 subtree
        result.Where(r => r.DescendantId == root2).Should().HaveCount(1);
        Row(result, root2, root2)!.Depth.Should().Be(0);

        // child1 should have self + parent
        result.Where(r => r.DescendantId == child1).Should().HaveCount(2);
        Row(result, child1, child1)!.Depth.Should().Be(0);
        Row(result, root1, child1)!.Depth.Should().Be(1);

        result.Should().HaveCount(4); // root1(1) + root2(1) + child1(2)
    }

    // ── move scenario ─────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenParentIdChangedAfterMove_YieldsNewClosureOldAncestorGone()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var team = Guid.NewGuid();

        // Before move: team under companyA
        var before = Compute(new (Guid, Guid?)[]
        {
            (companyA, null),
            (companyB, null),
            (team, companyA),
        });

        Row(before, companyA, team).Should().NotBeNull();
        Row(before, companyB, team).Should().BeNull();

        // After move: team under companyB
        var after = Compute(new (Guid, Guid?)[]
        {
            (companyA, null),
            (companyB, null),
            (team, companyB),
        });

        Row(after, companyA, team).Should().BeNull("old ancestor must be gone after move");
        Row(after, companyB, team)!.Depth.Should().Be(1, "team is now a direct child of companyB");
        Row(after, team, team)!.Depth.Should().Be(0);
    }

    // ── node with unknown parent ──────────────────────────────────────────────

    [Fact]
    public void Compute_WhenParentNotInSet_NodeTreatedAsRoot()
    {
        var knownParent = Guid.NewGuid();
        var unknownParent = Guid.NewGuid();
        var child = Guid.NewGuid();

        // child's parent is NOT in the node set
        var nodes = new (Guid Id, Guid? ParentId)[]
        {
            (knownParent, null),
            (child, unknownParent),
        };

        var result = Compute(nodes);

        // child should only have a self-row; unknown parent rows must NOT appear
        result.Where(r => r.DescendantId == child).Should().HaveCount(1);
        Row(result, child, child)!.Depth.Should().Be(0);
        result.Should().NotContain(r => r.AncestorId == unknownParent);
    }

    // ── null argument guard ───────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenNodesIsNull_ThrowsArgumentNullException()
    {
        var act = () => Compute(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("nodes");
    }

    // ── empty input ───────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenNodesIsEmpty_ReturnsEmptyCollection()
    {
        var result = Compute(Array.Empty<(Guid, Guid?)>());
        result.Should().BeEmpty();
    }

    // ── cyclic parent reference ───────────────────────────────────────────────

    [Fact]
    public void Compute_WhenParentReferencesFormCycle_ThrowsInvalidOperationException()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // a → b → a forms a cycle within the supplied node set
        var nodes = new (Guid Id, Guid? ParentId)[]
        {
            (a, b),
            (b, a),
        };

        var act = () => Compute(nodes);

        // The calculator serves more than one tree, so the message speaks of nodes, not org units.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*node '{a}'*")
            .Which.Message.Should().NotContain("organization unit");
    }

    // ── deeper chain ──────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenFourLevelChain_AllAncestorDepthsCorrect()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        var nodes = new (Guid Id, Guid? ParentId)[]
        {
            (a, null),
            (b, a),
            (c, b),
            (d, c),
        };

        var result = Compute(nodes);

        // d → c at 1, d → b at 2, d → a at 3
        Row(result, c, d)!.Depth.Should().Be(1);
        Row(result, b, d)!.Depth.Should().Be(2);
        Row(result, a, d)!.Depth.Should().Be(3);

        // Total: a(1) + b(2) + c(3) + d(4) = 10
        result.Should().HaveCount(10);
    }

    // ── row factory ───────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenCreateRowIsNull_ThrowsArgumentNullException()
    {
        var act = () => _sut.Compute<ClosureRow>(Array.Empty<(Guid, Guid?)>(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("createRow");
    }

    [Fact]
    public void Compute_WithCustomRowType_EmitsExactlyWhatTheFactoryBuilds()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var nodes = new (Guid Id, Guid? ParentId)[] { (root, null), (child, root) };

        // A row type unrelated to any closure entity: the calculator must hand the factory
        // (ancestor, descendant, depth) in that order and return its results untouched.
        var result = _sut.Compute(nodes, (ancestorId, descendantId, depth) => $"{ancestorId}>{descendantId}@{depth}");

        result.Should().BeEquivalentTo(
            $"{root}>{root}@0",
            $"{child}>{child}@0",
            $"{root}>{child}@1");
    }

    [Fact]
    public void Compute_WhenAParentPrecedesTheCycle_ThrowsForTheNodeWhoseWalkEntersIt()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var leaf = Guid.NewGuid();

        // The leaf is outside the cycle but its walk runs into it, which must still end in the
        // exception rather than a walk that never ends.
        var nodes = new (Guid Id, Guid? ParentId)[] { (leaf, a), (a, b), (b, a) };

        var act = () => Compute(nodes);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*node '{leaf}'*");
    }

    [Fact]
    public void Compute_WhenNodeIsItsOwnParent_ThrowsInvalidOperationException()
    {
        var a = Guid.NewGuid();

        var act = () => Compute(new (Guid, Guid?)[] { (a, a) });

        act.Should().Throw<InvalidOperationException>();
    }
}
