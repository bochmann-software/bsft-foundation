using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public class PostgresListenStatementTests
{
    [Fact]
    public void For_QuotesTheChannelSoItsCaseSurvives()
    {
        // An unquoted identifier folds to lower case on the server, while pg_notify takes the channel
        // as a literal. Quoting is what keeps the two halves naming the same channel.
        PostgresListenStatement.For("bsft_outbox").Should().Be("LISTEN \"bsft_outbox\";");
    }

    [Fact]
    public void For_NamesTheChannelItWasGiven()
    {
        PostgresListenStatement.For("orders_outbox").Should().Contain("orders_outbox");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1outbox")]
    [InlineData("Outbox")]
    [InlineData("out\"box")]
    [InlineData("out box")]
    public void For_RefusesAChannelNameOutsideTheGrammar(string channel)
    {
        // The grammar is what makes the concatenation safe, so the builder enforces it itself rather
        // than trusting that every caller came through options validation.
        var act = () => PostgresListenStatement.For(channel);

        act.Should().Throw<ArgumentException>().WithParameterName("channel");
    }

    [Fact]
    public void IsValidChannel_RefusesANameLongerThanTheServerKeeps()
    {
        PostgresListenStatement
            .IsValidChannel(new string('a', PostgresDefaults.MaxNotificationChannelLength + 1))
            .Should().BeFalse();
    }
}
