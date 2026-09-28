using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Tests.Support;

/// <summary>
/// Wraps a consumer or outbox option group in the <see cref="IOptions{TOptions}"/> the components
/// take, so a test states only the values it cares about.
/// </summary>
/// <remarks>
/// The groups on <see cref="BsftMessagingOptions"/> are get-only, which is what lets the configuration
/// binder fill them in place, so the values are copied onto the real instance rather than the instance
/// being assigned.
/// </remarks>
public static class TestMessagingOptions
{
    /// <summary>Wraps the given consumer options.</summary>
    /// <param name="consumer">The consumer values the test states.</param>
    /// <returns>Messaging options carrying them.</returns>
    public static IOptions<BsftMessagingOptions> For(ConsumerOptions consumer)
    {
        var options = new BsftMessagingOptions();
        Copy(consumer, options.Consumer);

        return Options.Create(options);
    }

    /// <summary>Wraps the given outbox options.</summary>
    /// <param name="outbox">The outbox values the test states.</param>
    /// <returns>Messaging options carrying them.</returns>
    public static IOptions<BsftMessagingOptions> For(OutboxOptions outbox)
    {
        var options = new BsftMessagingOptions();
        Copy(outbox, options.Outbox);

        return Options.Create(options);
    }

    /// <summary>Wraps a whole messaging options instance.</summary>
    /// <param name="options">The options to wrap.</param>
    /// <returns>The same options, wrapped.</returns>
    public static IOptions<BsftMessagingOptions> For(BsftMessagingOptions options) => Options.Create(options);

    private static void Copy(ConsumerOptions from, ConsumerOptions to)
    {
        to.ConsumerName = from.ConsumerName;
        to.MaxDeliveryAttempts = from.MaxDeliveryAttempts;
        to.ReceiveRestartDelay = from.ReceiveRestartDelay;
        to.InboxLedgerCapacity = from.InboxLedgerCapacity;
        to.ParkedMessageCapacity = from.ParkedMessageCapacity;
        to.RetryDelay = from.RetryDelay;
        to.MaxRetryDelay = from.MaxRetryDelay;
        to.InboxRetention = from.InboxRetention;
    }

    private static void Copy(OutboxOptions from, OutboxOptions to)
    {
        to.PollInterval = from.PollInterval;
        to.BatchSize = from.BatchSize;
        to.LeaseDuration = from.LeaseDuration;
        to.MaxDeliveryAttempts = from.MaxDeliveryAttempts;
        to.RetryBackoff = from.RetryBackoff;
        to.MaxRetryDelay = from.MaxRetryDelay;
    }
}
