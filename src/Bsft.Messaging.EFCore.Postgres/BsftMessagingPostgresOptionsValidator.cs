using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Runs the bounds of <see cref="BsftMessagingPostgresOptions"/> under <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// The channel name is the reason this is a validator rather than a data annotation. It is the one
/// value of this package that reaches the database as an identifier instead of as a parameter — a
/// <c>LISTEN</c> statement takes no parameter — so it is checked against a deliberately narrow grammar
/// at start-up, where a rejected value costs a failed boot rather than a statement built from it.
/// </remarks>
internal sealed class BsftMessagingPostgresOptionsValidator : IValidateOptions<BsftMessagingPostgresOptions>
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftMessagingPostgresOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        if (!PostgresListenStatement.IsValidChannel(options.NotificationChannel))
            failures.Add(
                $"{nameof(BsftMessagingPostgresOptions.NotificationChannel)} must be 1 to {PostgresDefaults.MaxNotificationChannelLength} characters of lower-case letters, digits and underscores, starting with a letter.");

        if (options.ReconnectDelay <= TimeSpan.Zero)
            failures.Add(
                $"{nameof(BsftMessagingPostgresOptions.ReconnectDelay)} must be positive, so a server that is restarting is not met with a reconnect storm.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
