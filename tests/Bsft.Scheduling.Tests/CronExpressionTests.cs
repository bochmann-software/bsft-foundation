using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class CronExpressionTests
{
    private static readonly DateTimeOffset _monday = new(2026, 8, 3, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void EveryMinuteFiresAtTheNextWholeMinute()
    {
        var next = CronExpression.Parse("* * * * *").FindNextOccurrence(_monday.AddSeconds(20));

        next.Should().Be(new DateTimeOffset(2026, 8, 3, 8, 31, 0, TimeSpan.Zero));
    }

    [Fact]
    public void TheResultIsAlwaysStrictlyLaterThanTheInput()
    {
        // A job that just ran at an exact match must not match its own occurrence again.
        var next = CronExpression.Parse("* * * * *").FindNextOccurrence(_monday);

        next.Should().Be(_monday.AddMinutes(1));
    }

    [Fact]
    public void ASingleValueDoesNotWidenIntoARange()
    {
        // "5" is minute five only. The step form "5/15" is what means "from five onwards".
        var expression = CronExpression.Parse("5 * * * *");

        expression.FindNextOccurrence(new DateTimeOffset(2026, 8, 3, 8, 5, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 8, 3, 9, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AStepFromAValueRunsToTheEndOfTheRange()
    {
        var expression = CronExpression.Parse("5/15 * * * *");

        expression.FindNextOccurrence(new DateTimeOffset(2026, 8, 3, 8, 5, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 8, 3, 8, 20, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AStepOnAWildcardStartsAtTheRangeMinimum()
    {
        var expression = CronExpression.Parse("*/15 * * * *");

        expression.FindNextOccurrence(new DateTimeOffset(2026, 8, 3, 8, 50, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AListSelectsEachOfItsValues()
    {
        var expression = CronExpression.Parse("0,30 * * * *");

        expression.FindNextOccurrence(new DateTimeOffset(2026, 8, 3, 8, 10, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 8, 3, 8, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ARangeWithAStepSelectsEveryNthValueInIt()
    {
        var expression = CronExpression.Parse("0-30/10 * * * *");

        expression.FindNextOccurrence(new DateTimeOffset(2026, 8, 3, 8, 21, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 8, 3, 8, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnHourRestrictionAdvancesToTheNextMatchingHour()
    {
        var expression = CronExpression.Parse("0 3 * * *");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AMonthRestrictionAdvancesWholeMonths()
    {
        var expression = CronExpression.Parse("0 0 1 1 *");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void MonthNamesAreAcceptedCaseInsensitively()
    {
        var expression = CronExpression.Parse("0 0 1 sep *");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DayNamesSelectWeekdays()
    {
        // 3 August 2026 is a Monday, so the next weekday run at 03:00 is Tuesday.
        var expression = CronExpression.Parse("0 3 * * MON-FRI");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AWeekendScheduleSkipsToSaturday()
    {
        var expression = CronExpression.Parse("0 0 * * SAT,SUN");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void SundayIsAcceptedAsBothZeroAndSeven()
    {
        var asZero = CronExpression.Parse("0 0 * * 0").FindNextOccurrence(_monday);
        var asSeven = CronExpression.Parse("0 0 * * 7").FindNextOccurrence(_monday);

        asZero.Should().Be(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero));
        asSeven.Should().Be(asZero);
    }

    [Fact]
    public void ARestrictedDayOfMonthAndDayOfWeekMatchWhenEitherDoes()
    {
        // Standard cron semantics: "the 5th OR any Friday", not the intersection.
        var expression = CronExpression.Parse("0 0 5 * FRI");

        // 3 August 2026 is a Monday; the 5th is the Wednesday, which matches on day-of-month alone.
        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ARestrictedDayOfMonthAloneIgnoresTheWeekday()
    {
        var expression = CronExpression.Parse("0 0 5 * *");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnExpressionThatCanNeverMatchReturnsNull()
    {
        // The thirtieth of February is a valid expression with no occurrence.
        CronExpression.Parse("0 0 30 2 *").FindNextOccurrence(_monday).Should().BeNull();
    }

    [Fact]
    public void TheTwentyNinthOfFebruaryIsFoundInTheNextLeapYear()
    {
        CronExpression.Parse("0 0 29 2 *").FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnInputWithANonZeroOffsetIsInterpretedInUtc()
    {
        var berlinNoon = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.FromHours(2));

        CronExpression.Parse("0 * * * *").FindNextOccurrence(berlinNoon)
            .Should().Be(new DateTimeOffset(2026, 8, 3, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ExtraWhitespaceBetweenFieldsIsIgnored()
    {
        var expression = CronExpression.Parse("0   3  *  * *");

        expression.FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void TheExpressionTextIsPreserved()
    {
        var expression = CronExpression.Parse("0 3 * * *");

        expression.Expression.Should().Be("0 3 * * *");
        expression.ToString().Should().Be("0 3 * * *");
    }

    [Fact]
    public void ParseRejectsNull()
    {
        var act = () => CronExpression.Parse(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("expression");
    }

    [Theory]
    [InlineData("* * * *", "5 fields")]
    [InlineData("* * * * * *", "5 fields")]
    [InlineData("", "5 fields")]
    public void ParseRejectsAWrongFieldCount(string expression, string expectedFragment)
    {
        var act = () => CronExpression.Parse(expression);

        act.Should().Throw<CronFormatException>().WithMessage($"*{expectedFragment}*");
    }

    [Theory]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * 32 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    public void ParseRejectsAValueOutsideTheFieldRange(string expression)
    {
        var act = () => CronExpression.Parse(expression);

        act.Should().Throw<CronFormatException>().WithMessage("*outside the range*");
    }

    [Theory]
    [InlineData("abc * * * *")]
    [InlineData("* * * XYZ *")]
    public void ParseRejectsAnUnrecognisedValue(string expression)
    {
        var act = () => CronExpression.Parse(expression);

        act.Should().Throw<CronFormatException>().WithMessage("*unrecognised value*");
    }

    [Theory]
    [InlineData("*/0 * * * *")]
    [InlineData("*/x * * * *")]
    [InlineData("*/-1 * * * *")]
    public void ParseRejectsAnInvalidStep(string expression)
    {
        var act = () => CronExpression.Parse(expression);

        act.Should().Throw<CronFormatException>().WithMessage("*invalid step*");
    }

    [Theory]
    [InlineData("23/2147483647 * * * *")]
    [InlineData("*/2147483647 * * * *")]
    [InlineData("* 23/2147483647 * * *")]
    [InlineData("*/60 * * * *")]
    public void AnOversizedStepIsRejectedRatherThanOverflowing(string expression)
    {
        // Adding an unbounded step to the running value wraps int around to a large negative number
        // that still passes the loop's upper-bound test, which would index the value map out of
        // bounds and throw the one exception type this parser promises never to raise.
        var act = () => CronExpression.Parse(expression);

        act.Should().Throw<CronFormatException>().WithMessage("*invalid step*");
    }

    [Theory]
    [InlineData("0 0 * * /2")]
    [InlineData("/2 * * * *")]
    [InlineData("* /15 * * *")]
    public void AStepWithNoValueInFrontOfItIsRejectedAsAFormatError(string expression)
    {
        // A term starting with the step separator left the range spec empty, and the hyphen search
        // then read index one of an empty string. That raised ArgumentOutOfRangeException, which
        // TryParse does not catch, so validating a configured schedule crashed instead of reporting
        // the expression as malformed. An empty term between two commas was already refused.
        var act = () => CronExpression.Parse(expression);

        act.Should().Throw<CronFormatException>().WithMessage("*step with no value*");
    }

    [Fact]
    public void TryParseStaysBooleanForAStepWithNoValueInFrontOfIt()
    {
        CronExpression.TryParse("0 0 * * /2", out var parsed).Should().BeFalse();

        parsed.Should().BeNull();
    }

    [Fact]
    public void TryParseStaysBooleanForAnOversizedStep()
    {
        CronExpression.TryParse("23/2147483647 * * * *", out var expression).Should().BeFalse();

        expression.Should().BeNull();
    }

    [Fact]
    public void AStepAsWideAsTheFieldIsStillAccepted()
    {
        // 59 is the exact span of the minute field, so it must sit just inside the bound.
        CronExpression.Parse("*/59 * * * *").FindNextOccurrence(_monday)
            .Should().Be(new DateTimeOffset(2026, 8, 3, 8, 59, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ParseRejectsADescendingRange()
    {
        var act = () => CronExpression.Parse("30-10 * * * *");

        act.Should().Throw<CronFormatException>().WithMessage("*descending range*");
    }

    [Fact]
    public void ParseRejectsAnEmptyTermInAList()
    {
        var act = () => CronExpression.Parse("1,,2 * * * *");

        act.Should().Throw<CronFormatException>().WithMessage("*empty term*");
    }

    [Fact]
    public void ParseRejectsTheNonStandardExtensionsOfLargerLibraries()
    {
        // "L", "W" and "#" are deliberately unsupported and must fail loudly, not be ignored.
        var act = () => CronExpression.Parse("0 0 L * *");

        act.Should().Throw<CronFormatException>();
    }

    [Fact]
    public void TryParseReturnsTheExpressionForAValidText()
    {
        CronExpression.TryParse("0 3 * * *", out var expression).Should().BeTrue();

        expression.Should().NotBeNull();
        expression!.Expression.Should().Be("0 3 * * *");
    }

    [Fact]
    public void TryParseReportsAMalformedTextWithoutThrowing()
    {
        CronExpression.TryParse("nonsense", out var expression).Should().BeFalse();

        expression.Should().BeNull();
    }

    [Fact]
    public void TryParseStillThrowsForNullBecauseThatIsACallerBug()
    {
        var act = () => CronExpression.TryParse(null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TheFormatExceptionOffersTheStandardConstructors()
    {
        new CronFormatException().Message.Should().NotBeNull();
        new CronFormatException("bad").Message.Should().Be("bad");

        var inner = new InvalidOperationException("inner");
        var wrapped = new CronFormatException("bad", inner);

        wrapped.Message.Should().Be("bad");
        wrapped.InnerException.Should().BeSameAs(inner);
    }
}
