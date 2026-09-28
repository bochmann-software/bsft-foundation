using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.Tests;

public class BsftMessagingOptionsValidatorTests
{
    private readonly IValidateOptions<BsftMessagingOptions> _validator = new BsftMessagingOptionsValidator();

    [Fact]
    public void The_defaults_pass()
    {
        _validator.Validate(null, new BsftMessagingOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_missing_consumer_name_is_not_a_validation_failure()
    {
        // It is required only by the consuming pipeline, so a host that publishes without consuming
        // must not be refused its start-up over a value it has no use for.
        var options = new BsftMessagingOptions();
        options.Consumer.ConsumerName = string.Empty;

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_attempt_budget_below_one_is_refused_on_both_sides(int attempts)
    {
        var options = new BsftMessagingOptions();
        options.Consumer.MaxDeliveryAttempts = attempts;
        options.Outbox.MaxDeliveryAttempts = attempts;

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2);
    }

    [Fact]
    public void Every_complaint_is_reported_in_one_go()
    {
        var options = new BsftMessagingOptions();
        options.Consumer.ReceiveRestartDelay = TimeSpan.Zero;
        options.Consumer.InboxLedgerCapacity = 0;
        options.Consumer.ParkedMessageCapacity = 0;
        options.Consumer.RetryDelay = TimeSpan.FromSeconds(-1);
        options.Consumer.InboxRetention = TimeSpan.Zero;
        options.Outbox.BatchSize = 0;
        options.Outbox.PollInterval = TimeSpan.Zero;
        options.Outbox.LeaseDuration = TimeSpan.Zero;
        options.Outbox.RetryBackoff = TimeSpan.FromSeconds(-1);

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_retry_ceiling_that_clamps_every_wait_away_is_refused_on_both_sides(int ceilingSeconds)
    {
        // A ceiling of nothing would clamp even a wait a handler explicitly asked for back to zero,
        // which is a retry schedule that silently is not one.
        var options = new BsftMessagingOptions();
        options.Consumer.MaxRetryDelay = TimeSpan.FromSeconds(ceilingSeconds);
        options.Outbox.MaxRetryDelay = TimeSpan.FromSeconds(ceilingSeconds);

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2);
    }

    [Fact]
    public void A_retry_backoff_of_zero_is_the_immediate_retry_of_earlier_releases_and_passes()
    {
        var options = new BsftMessagingOptions();
        options.Outbox.RetryBackoff = TimeSpan.Zero;

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_set_inbox_retention_that_is_positive_passes()
    {
        var options = new BsftMessagingOptions();
        options.Consumer.InboxRetention = TimeSpan.FromDays(7);

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_null_options_instance_is_rejected()
    {
        var act = () => _validator.Validate(null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
