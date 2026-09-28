using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests;

public sealed class RetentionStaggerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    [Fact]
    public void OffsetFor_SameName_IsTheSameEveryTime()
    {
        // Determinism is the whole point: a pass lands at the same point of the window on every
        // instance and every restart, so a deployment's sweeps stay where an operator last saw them.
        var first = RetentionStagger.OffsetFor("Contoso.AuditLogRetentionPass", Window);
        var second = RetentionStagger.OffsetFor("Contoso.AuditLogRetentionPass", Window);

        second.Should().Be(first);
    }

    [Fact]
    public void OffsetFor_DifferentNames_Diverge()
    {
        // The property that matters is that two passes differ, so they do not converge on one instant.
        var names = new[]
        {
            "Contoso.AuditLogRetentionPass",
            "Contoso.InboxRetentionPass",
            "Contoso.WebhookDeliveryRetentionPass",
            "Contoso.StorageRetentionPass",
            "Contoso.SessionRetentionPass"
        };

        var offsets = names.Select(name => RetentionStagger.OffsetFor(name, Window)).ToList();

        offsets.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void OffsetFor_AlwaysFallsInsideTheWindow()
    {
        var names = Enumerable.Range(0, 200).Select(i => $"Contoso.Pass{i}");

        foreach (var name in names)
        {
            var offset = RetentionStagger.OffsetFor(name, Window);

            offset.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            offset.Should().BeLessThan(Window);
        }
    }

    [Fact]
    public void OffsetFor_ZeroWindow_IsNoOffset()
    {
        var offset = RetentionStagger.OffsetFor("Contoso.AuditLogRetentionPass", TimeSpan.Zero);

        offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void OffsetFor_NegativeWindow_IsNoOffset()
    {
        // The validator rejects a negative window, so this is only ever reached by a direct call; it
        // returns no offset rather than throwing out of a background service's first moment.
        var offset = RetentionStagger.OffsetFor("Contoso.AuditLogRetentionPass", TimeSpan.FromMinutes(-1));

        offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void OffsetFor_NullName_Throws()
    {
        var act = () => RetentionStagger.OffsetFor(null!, Window);

        act.Should().Throw<ArgumentNullException>().WithParameterName("passName");
    }

    [Fact]
    public void OffsetFor_EmptyName_IsInsideTheWindow()
    {
        var offset = RetentionStagger.OffsetFor(string.Empty, Window);

        offset.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        offset.Should().BeLessThan(Window);
    }
}
