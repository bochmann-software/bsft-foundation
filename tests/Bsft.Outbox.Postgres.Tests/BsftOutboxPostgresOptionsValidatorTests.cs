using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.Postgres.Tests;

/// <summary>Tests what the host is told at start about a wake-up it cannot run.</summary>
public sealed class BsftOutboxPostgresOptionsValidatorTests
{
    private static readonly BsftOutboxPostgresOptionsValidator Validator = new();

    private static BsftOutboxPostgresOptions Valid() => new()
    {
        NotificationChannel = "orders_outbox",
        ConnectionString = "Host=localhost",
    };

    [Fact]
    public void Validate_WithAChannelAndAConnection_Succeeds()
    {
        Validator.Validate("orders", Valid()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithTheDefaults_FailsOnTheChannelAndTheConnection()
    {
        var result = Validator.Validate("orders", new BsftOutboxPostgresOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2)
            .And.Contain(failure => failure.Contains(nameof(BsftOutboxPostgresOptions.NotificationChannel)))
            .And.Contain(failure => failure.Contains(nameof(BsftOutboxPostgresOptions.ConnectionString)));
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("1orders")]
    [InlineData("or\"ders")]
    public void Validate_WithAChannelOutsideTheGrammar_Fails(string channel)
    {
        var options = Valid();
        options.NotificationChannel = channel;

        var result = Validator.Validate("orders", options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("orders").And.Contain(nameof(BsftOutboxPostgresOptions.NotificationChannel));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void Validate_WithAReconnectDelayThatIsNotPositive_Fails(string delay)
    {
        var options = Valid();
        options.ReconnectDelay = TimeSpan.Parse(delay);

        Validator.Validate("orders", options).FailureMessage.Should().Contain(nameof(BsftOutboxPostgresOptions.ReconnectDelay));
    }

    [Fact]
    public void Validate_WithoutAName_FailsWithoutNamingAnOutbox()
    {
        var result = Validator.Validate(null, new BsftOutboxPostgresOptions());

        result.FailureMessage.Should().NotContain("named");
    }

    [Fact]
    public void Validate_WithoutOptions_ThrowsArgumentNullException()
    {
        var act = () => Validator.Validate("orders", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
