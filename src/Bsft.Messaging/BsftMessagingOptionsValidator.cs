using Microsoft.Extensions.Options;

namespace Bsft.Messaging;

/// <summary>
/// Runs the bounds of <see cref="BsftMessagingOptions"/> and of its two groups under
/// <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IValidateOptions{TOptions}"/> rather than data annotations because the values live on
/// two nested groups, which an attribute on the root type cannot reach, and because every failure is
/// collected rather than thrown one at a time: a misconfigured host is told everything that is wrong
/// with its section in one go instead of over several restarts.
/// </para>
/// <para>
/// <see cref="ConsumerOptions.ConsumerName"/> is deliberately not checked here. It is required only by
/// the consuming pipeline, and a host that publishes without consuming configures no consumer name at
/// all; failing its start-up over a value it has no use for would be wrong.
/// <c>IntegrationEventConsumerHost</c> rejects an absent name instead, which is the component that
/// actually needs it.
/// </para>
/// </remarks>
internal sealed class BsftMessagingOptionsValidator : IValidateOptions<BsftMessagingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftMessagingOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        Validate(options.Consumer, failures);
        Validate(options.Outbox, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Rejects consumer options that would make the consuming side misbehave rather than fail.
    /// </summary>
    /// <param name="options">The consumer options to check.</param>
    /// <param name="failures">The list every complaint is added to.</param>
    /// <remarks>
    /// An attempt limit below one would park every message on its first delivery, before any handler
    /// ever ran. A non-positive restart delay turns a source that cannot connect into a tight reconnect
    /// loop. An inbox ledger capacity below one would leave the in-memory ledger evicting every record
    /// it takes, so no redelivery is ever recognised as one. A parked message capacity below one would
    /// drop every parked message the loopback was asked to keep, which is the one outcome that has
    /// nothing else holding it. A negative retry delay makes the delay itself throw on the first failed
    /// message, and an inbox retention of zero or less would expire every record the moment it was
    /// written, turning the durable inbox into no inbox at all. A retry ceiling of zero or less would
    /// clamp every wait — including one a handler explicitly asked for — back to nothing, which is a
    /// retry schedule that silently is not one.
    /// </remarks>
    private static void Validate(ConsumerOptions options, List<string> failures)
    {
        if (options.MaxDeliveryAttempts < 1)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.MaxDeliveryAttempts)} must be at least 1, but it is {options.MaxDeliveryAttempts}.");

        if (options.ReceiveRestartDelay <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.ReceiveRestartDelay)} must be positive, but it is {options.ReceiveRestartDelay}.");

        if (options.InboxLedgerCapacity < 1)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.InboxLedgerCapacity)} must be at least 1, but it is {options.InboxLedgerCapacity}.");

        if (options.ParkedMessageCapacity < 1)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.ParkedMessageCapacity)} must be at least 1, but it is {options.ParkedMessageCapacity}.");

        if (options.RetryDelay < TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.RetryDelay)} must not be negative, but it is {options.RetryDelay}.");

        if (options.MaxRetryDelay <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.MaxRetryDelay)} must be positive, but it is {options.MaxRetryDelay}.");

        if (options.InboxRetention is { } retention && retention <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Consumer)}.{nameof(ConsumerOptions.InboxRetention)} must be positive when it is set, but it is {retention}.");
    }

    /// <summary>
    /// Rejects outbox options that would break the dispatch loop silently or loudly.
    /// </summary>
    /// <param name="options">The outbox options to check.</param>
    /// <param name="failures">The list every complaint is added to.</param>
    /// <remarks>
    /// A non-positive batch size makes every poll claim nothing, so the outbox would simply never
    /// drain; a non-positive poll interval turns an idle dispatcher into a tight query loop; a
    /// non-positive lease expires the moment it is taken, which hands every message to every instance
    /// at once; an attempt limit below one would retire every message on its first failure; a negative
    /// backoff would compute a wait in the past on every failure, and a retry ceiling of zero or less
    /// would clamp every wait back to nothing, including one a handler explicitly asked for.
    /// </remarks>
    private static void Validate(OutboxOptions options, List<string> failures)
    {
        if (options.BatchSize < 1)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Outbox)}.{nameof(OutboxOptions.BatchSize)} must be at least 1, but it is {options.BatchSize}.");

        if (options.PollInterval <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Outbox)}.{nameof(OutboxOptions.PollInterval)} must be positive, but it is {options.PollInterval}.");

        if (options.LeaseDuration <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Outbox)}.{nameof(OutboxOptions.LeaseDuration)} must be positive, but it is {options.LeaseDuration}.");

        if (options.MaxDeliveryAttempts < 1)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Outbox)}.{nameof(OutboxOptions.MaxDeliveryAttempts)} must be at least 1, but it is {options.MaxDeliveryAttempts}.");

        if (options.RetryBackoff < TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Outbox)}.{nameof(OutboxOptions.RetryBackoff)} must not be negative, but it is {options.RetryBackoff}.");

        if (options.MaxRetryDelay <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingOptions.Outbox)}.{nameof(OutboxOptions.MaxRetryDelay)} must be positive, but it is {options.MaxRetryDelay}.");
    }
}
