using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

public class UnitTests
{
    [Fact]
    public void Equals_Unit_ReturnsTrue()
    {
        var a = Unit.Value;
        var b = Unit.Value;

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_Object_Unit_ReturnsTrue()
    {
        var unit = Unit.Value;
        object obj = Unit.Value;

        unit.Equals(obj).Should().BeTrue();
    }

    [Fact]
    public void Equals_Object_NonUnit_ReturnsFalse()
    {
        var unit = Unit.Value;

        unit.Equals("not a unit").Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ReturnsZero()
    {
        Unit.Value.GetHashCode().Should().Be(0);
    }

    [Fact]
    public void CompareTo_ReturnsZero()
    {
        Unit.Value.CompareTo(Unit.Value).Should().Be(0);
    }

    [Fact]
    public void ToString_ReturnsParentheses()
    {
        Unit.Value.ToString().Should().Be("()");
    }

    [Fact]
    public void OperatorEquals_ReturnsTrue()
    {
        var a = Unit.Value;
        var b = Unit.Value;
#pragma warning disable CS1718
        (a == b).Should().BeTrue();
#pragma warning restore CS1718
    }

    [Fact]
    public void OperatorNotEquals_ReturnsFalse()
    {
        var a = Unit.Value;
        var b = Unit.Value;
#pragma warning disable CS1718
        (a != b).Should().BeFalse();
#pragma warning restore CS1718
    }

    [Fact]
    public async Task Task_IsCompletedWithUnitValue()
    {
        var result = await Unit.Task;
        Unit.Task.IsCompleted.Should().BeTrue();
        result.Should().Be(Unit.Value);
    }

    [Fact]
    public async Task TheNamesTheTypeFirstShippedWith_StillAnswerWithTheSameValues()
    {
        // Kept compiling on purpose: application code that named the screaming-case members predates
        // the rename, and a struct that stands in for "no result" is not worth a breaking change.
#pragma warning disable CS0618
        Unit.VALUE.Should().Be(Unit.Value);
        Unit.TASK.Should().BeSameAs(Unit.Task);
        (await Unit.TASK).Should().Be(Unit.Value);
#pragma warning restore CS0618
    }

    [Fact]
    public void RelationalOperators_ReportEveryInstanceAsEqual()
    {
        var a = Unit.Value;
        var b = Unit.Value;

#pragma warning disable CS1718
        (a < b).Should().BeFalse();
        (a > b).Should().BeFalse();
        (a <= b).Should().BeTrue();
        (a >= b).Should().BeTrue();
#pragma warning restore CS1718
    }
}
