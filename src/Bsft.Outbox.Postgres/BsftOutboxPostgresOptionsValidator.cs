using Microsoft.Extensions.Options;

namespace Bsft.Outbox.Postgres;

/// <summary>Runs the bounds of <see cref="BsftOutboxPostgresOptions"/> under <c>ValidateOnStart()</c>.</summary>
/// <remarks>
/// The channel name is the reason this checks more than presence. It is the one value of this package
/// that reaches the database as an identifier instead of as a parameter, so it is checked against a
/// deliberately narrow grammar at start-up, where a rejected value costs a failed boot rather than a
/// statement built from it.
/// </remarks>
internal sealed class BsftOutboxPostgresOptionsValidator : IValidateOptions<BsftOutboxPostgresOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BsftOutboxPostgresOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var subject = name is null ? "The outbox wake-up" : $"The wake-up of the outbox named '{name}'";
        var failures = new List<string>();

        if (!PostgresNotificationChannel.IsValid(options.NotificationChannel))
        {
            failures.Add(
                $"{subject}: {nameof(BsftOutboxPostgresOptions.NotificationChannel)} must be 1 to {PostgresNotificationChannel.MaxLength} characters of lower-case letters, digits and underscores, starting with a letter.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add($"{subject}: {nameof(BsftOutboxPostgresOptions.ConnectionString)} must be set.");
        }

        if (options.ReconnectDelay <= TimeSpan.Zero)
        {
            failures.Add(
                $"{subject}: {nameof(BsftOutboxPostgresOptions.ReconnectDelay)} must be positive, so a server that is restarting is not met with a reconnect storm.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
