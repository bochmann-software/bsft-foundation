using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Abstractions.Tests;

/// <summary>The nesting marks of a behavior built without a scope: none, whatever is marked.</summary>
public sealed class NullRequestNestingTests
{
    [Fact]
    public void Mark_WhenCalled_ReportsNoOuterRequestAfterwards()
    {
        var nesting = NullRequestNesting.Instance;

        nesting.MarkRetrying();
        nesting.MarkTransactionOpen();

        nesting.IsRetrying.Should().BeFalse();
        nesting.IsTransactionOpen.Should().BeFalse();
    }
}
