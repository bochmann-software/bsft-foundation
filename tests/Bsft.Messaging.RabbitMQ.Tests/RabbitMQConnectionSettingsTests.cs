using Bsft.Messaging.RabbitMQ.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Messaging.RabbitMQ.Tests;

public class RabbitMQConnectionSettingsTests
{
    [Fact]
    public void Valid_options_are_carried_through_unchanged()
    {
        var options = TestOptions.Valid();
        options.HostName = "broker.internal";
        options.Port = 5673;
        options.VirtualHost = "/orders";
        options.PrefetchCount = 5;
        options.PublisherConfirmTimeout = TimeSpan.FromSeconds(3);
        options.NetworkRecoveryInterval = TimeSpan.FromSeconds(1);
        options.ContentType = "application/x-custom";
        options.Heartbeat = TimeSpan.FromSeconds(15);
        options.ContinuationTimeout = TimeSpan.FromSeconds(7);
        options.PublishChannelCount = 4;

        var settings = new RabbitMQConnectionSettings(options);

        settings.Heartbeat.Should().Be(TimeSpan.FromSeconds(15));
        settings.ContinuationTimeout.Should().Be(TimeSpan.FromSeconds(7));
        settings.PublishChannelCount.Should().Be(4);

        settings.ApplicationName.Should().Be("orders");
        settings.HostName.Should().Be("broker.internal");
        settings.Port.Should().Be(5673);
        settings.VirtualHost.Should().Be("/orders");
        settings.UserName.Should().Be(TestOptions.UserName);
        settings.Password.Should().Be(TestOptions.Password);
        settings.PrefetchCount.Should().Be(5);
        settings.PublisherConfirmTimeout.Should().Be(TimeSpan.FromSeconds(3));
        settings.NetworkRecoveryInterval.Should().Be(TimeSpan.FromSeconds(1));
        settings.ContentType.Should().Be("application/x-custom");
    }

    [Fact]
    public void A_null_options_object_is_rejected()
    {
        var construct = () => new RabbitMQConnectionSettings(null!);

        construct.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("ApplicationName")]
    [InlineData("HostName")]
    [InlineData("VirtualHost")]
    [InlineData("ContentType")]
    public void A_required_text_setting_may_not_be_blank(string property)
    {
        var options = TestOptions.Valid();
        Blank(options, property);

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentException>().WithMessage($"*{property} is required*");
    }

    [Fact]
    public void A_missing_user_name_is_refused_rather_than_defaulted()
    {
        var options = TestOptions.Valid();
        options.UserName = null;

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentException>()
            .WithMessage("*UserName is required*no credential default on purpose*");
    }

    [Fact]
    public void A_missing_password_is_refused_rather_than_defaulted()
    {
        var options = TestOptions.Valid();
        options.Password = "  ";

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentException>()
            .WithMessage("*Password is required*no credential default on purpose*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_the_tcp_range_is_rejected(int port)
    {
        var options = TestOptions.Valid();
        options.Port = port;

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Port");
    }

    [Fact]
    public void A_prefetch_of_zero_is_rejected_because_amqp_reads_it_as_no_limit()
    {
        var options = TestOptions.Valid();
        options.PrefetchCount = 0;

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("PrefetchCount")
            .WithMessage("*unlimited prefetch*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_confirm_timeout_that_is_not_positive_is_rejected(int seconds)
    {
        var options = TestOptions.Valid();
        options.PublisherConfirmTimeout = TimeSpan.FromSeconds(seconds);

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("PublisherConfirmTimeout");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_recovery_interval_that_is_not_positive_is_rejected(int seconds)
    {
        var options = TestOptions.Valid();
        options.NetworkRecoveryInterval = TimeSpan.FromSeconds(seconds);

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("NetworkRecoveryInterval");
    }

    [Fact]
    public void A_host_that_says_nothing_about_tls_connects_without_it()
    {
        var settings = new RabbitMQConnectionSettings(TestOptions.Valid());

        settings.UseTls.Should().BeFalse();
        settings.Heartbeat.Should().Be(TimeSpan.FromSeconds(60));
        settings.ContinuationTimeout.Should().Be(TimeSpan.FromSeconds(20));
        settings.PublishChannelCount.Should().Be(1);
    }

    [Fact]
    public void The_certificate_is_checked_against_the_host_being_connected_to_unless_told_otherwise()
    {
        var options = TestOptions.Valid();
        options.UseTls = true;
        options.HostName = "broker.internal";

        var settings = new RabbitMQConnectionSettings(options);

        settings.UseTls.Should().BeTrue();
        settings.TlsServerName.Should().Be("broker.internal");
    }

    [Theory]
    [InlineData("rabbit.example.com")]
    [InlineData("   ")]
    public void A_stated_certificate_name_wins_over_the_host_name_unless_it_says_nothing(string serverName)
    {
        // It exists for a broker reached under a name its certificate does not carry — through a load
        // balancer, or by address. Blank is not such a name, so it falls back rather than verifying
        // against nothing.
        var options = TestOptions.Valid();
        options.HostName = "10.0.0.7";
        options.TlsServerName = serverName;

        var settings = new RabbitMQConnectionSettings(options);

        settings.TlsServerName.Should().Be(string.IsNullOrWhiteSpace(serverName) ? "10.0.0.7" : serverName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_heartbeat_that_is_not_positive_is_rejected(int seconds)
    {
        var options = TestOptions.Valid();
        options.Heartbeat = TimeSpan.FromSeconds(seconds);

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Heartbeat");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_continuation_timeout_that_is_not_positive_is_rejected(int seconds)
    {
        var options = TestOptions.Valid();
        options.ContinuationTimeout = TimeSpan.FromSeconds(seconds);

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ContinuationTimeout");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_publishing_side_with_no_channel_to_publish_on_is_rejected(int count)
    {
        var options = TestOptions.Valid();
        options.PublishChannelCount = count;

        var construct = () => new RabbitMQConnectionSettings(options);

        construct.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("PublishChannelCount")
            .WithMessage("*at least 1*");
    }

    [Fact]
    public void The_settings_do_not_render_the_password_into_their_text_form()
    {
        var settings = new RabbitMQConnectionSettings(TestOptions.Valid());

        settings.ToString().Should().NotContain(TestOptions.Password);
    }

    private static void Blank(BsftRabbitMQOptions options, string property)
    {
        switch (property)
        {
            case "ApplicationName":
                options.ApplicationName = string.Empty;
                break;
            case "HostName":
                options.HostName = "   ";
                break;
            case "VirtualHost":
                options.VirtualHost = string.Empty;
                break;
            default:
                options.ContentType = string.Empty;
                break;
        }
    }
}
