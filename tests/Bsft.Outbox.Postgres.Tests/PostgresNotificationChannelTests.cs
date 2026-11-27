using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.Postgres.Tests;

public class PostgresNotificationChannelTests
{
    [Theory]
    [InlineData("bsft_outbox")]
    [InlineData("a")]
    [InlineData("orders_outbox_2")]
    public void IsValid_WithANameInTheGrammar_IsTrue(string name)
    {
        PostgresNotificationChannel.IsValid(name).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1outbox")]
    [InlineData("_outbox")]
    [InlineData("Outbox")]
    [InlineData("out\"box")]
    [InlineData("out box")]
    [InlineData("out-box")]
    [InlineData("out;box")]
    public void IsValid_WithANameOutsideTheGrammar_IsFalse(string? name)
    {
        PostgresNotificationChannel.IsValid(name).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithTheLongestNameTheServerKeeps_IsTrue()
    {
        PostgresNotificationChannel.IsValid(new string('a', PostgresNotificationChannel.MaxLength)).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithANameLongerThanTheServerKeeps_IsFalse()
    {
        PostgresNotificationChannel.IsValid(new string('a', PostgresNotificationChannel.MaxLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithAValidName_KeepsIt()
    {
        var channel = new PostgresNotificationChannel("orders_outbox");

        channel.Name.Should().Be("orders_outbox");
        channel.ToString().Should().Be("orders_outbox");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Orders; DROP")]
    public void Constructor_WithANameOutsideTheGrammar_ThrowsNamingTheParameter(string name)
    {
        // The grammar is checked by the type itself rather than trusting that every caller came through
        // options validation.
        var act = () => new PostgresNotificationChannel(name);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void Name_OfTheDefaultValue_IsEmpty()
    {
        default(PostgresNotificationChannel).Name.Should().BeEmpty();
    }

    [Fact]
    public void Equality_ComparesByName()
    {
        var first = new PostgresNotificationChannel("orders_outbox");
        var same = new PostgresNotificationChannel("orders_outbox");
        var other = new PostgresNotificationChannel("invoices_outbox");

        (first == same).Should().BeTrue();
        (first != other).Should().BeTrue();
        first.Equals((object)same).Should().BeTrue();
        first.Equals((object)"orders_outbox").Should().BeFalse();
        first.GetHashCode().Should().Be(same.GetHashCode());
    }
}
