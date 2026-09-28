using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public class BsftMessagingPostgresOptionsValidatorTests
{
    private readonly BsftMessagingPostgresOptionsValidator _validator = new();

    private BsftMessagingPostgresOptions Valid() => new();

    [Fact]
    public void Validate_TheDefaults_AreAccepted()
    {
        var options = Valid();

        options.NotificationChannel.Should().Be(PostgresDefaults.NotificationChannel);
        options.ConnectionString.Should().BeNull();
        options.ReconnectDelay.Should().Be(TimeSpan.FromSeconds(5));

        _validator.Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("orders_outbox")]
    [InlineData("a")]
    [InlineData("bsft_outbox_2")]
    public void Validate_AChannelInTheGrammar_IsAccepted(string channel)
    {
        var options = Valid();
        options.NotificationChannel = channel;

        _validator.Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Orders")]
    [InlineData("2orders")]
    [InlineData("_orders")]
    [InlineData("orders-outbox")]
    [InlineData("orders\"; DROP TABLE x; --")]
    public void Validate_AChannelOutsideTheGrammar_IsRejected(string channel)
    {
        // The name is quoted into a LISTEN statement, which takes an identifier and not a parameter, so
        // the grammar is what stands between an operator's typo and a statement this package assembled
        // out of it. Uppercase is refused as well, because an unquoted identifier folds to lower case
        // and a channel listened for as "Orders" would never see a pg_notify('orders').
        var options = Valid();
        options.NotificationChannel = channel;

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftMessagingPostgresOptions.NotificationChannel));
    }

    [Fact]
    public void Validate_AChannelThePostgresServerWouldTruncate_IsRejected()
    {
        var options = Valid();
        options.NotificationChannel = new string('a', PostgresDefaults.MaxNotificationChannelLength + 1);

        _validator.Validate(name: null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_AChannelAtTheServersLimit_IsAccepted()
    {
        var options = Valid();
        options.NotificationChannel = new string('a', PostgresDefaults.MaxNotificationChannelLength);

        _validator.Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ANonPositiveReconnectDelay_IsRejected(int seconds)
    {
        var options = Valid();
        options.ReconnectDelay = TimeSpan.FromSeconds(seconds);

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftMessagingPostgresOptions.ReconnectDelay));
    }

    [Fact]
    public void Validate_ReportsEveryFailureAtOnce()
    {
        var options = new BsftMessagingPostgresOptions
        {
            NotificationChannel = "NOPE",
            ReconnectDelay = TimeSpan.Zero,
        };

        var result = _validator.Validate(name: null, options);

        result.Failures.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_RejectsMissingOptions()
    {
        var validate = () => _validator.Validate(name: null, options: null!);

        validate.Should().Throw<ArgumentNullException>();
    }
}
