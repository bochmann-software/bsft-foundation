namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// The constants a host may need to state for itself when it wires the PostgreSQL wake-up.
/// </summary>
public static class PostgresDefaults
{
    /// <summary>
    /// The PostgreSQL notification channel the outbox is announced on. Every writer and every listener
    /// of one database has to name the same channel, which is why it is a constant rather than a value
    /// derived per application: two deployments of the same database that disagree about it would each
    /// wake only themselves and fall back to polling for everything the other wrote.
    /// </summary>
    public const string NotificationChannel = "bsft_outbox";

    /// <summary>
    /// The longest a channel name may be. PostgreSQL truncates an identifier at <c>NAMEDATALEN - 1</c>,
    /// which is 63 characters in a stock server, and a truncated <c>LISTEN</c> would silently stop
    /// matching the <c>pg_notify</c> the writer sends.
    /// </summary>
    public const int MaxNotificationChannelLength = 63;
}
