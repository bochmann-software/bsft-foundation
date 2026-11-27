namespace Bsft.Outbox.Postgres;

/// <summary>
/// What the PostgreSQL wake-up of one outbox needs: the channel its writers announce on, the
/// connection that listens for it, and how long the listener waits before it comes back after the
/// connection dropped.
/// </summary>
/// <remarks>
/// Resolved per outbox as named options, like <c>BsftOutboxOptions</c>: the section is
/// <see cref="SectionName"/> and the outbox's name is the sub-key underneath it. Nothing here changes
/// what is applied or how often a row is retried. The wake-up only shortens the wait between a commit
/// and the drain that follows it, and the worker's poll interval stays the guarantee whenever a
/// notification does not arrive.
/// </remarks>
public sealed class BsftOutboxPostgresOptions
{
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>The parent section one outbox's wake-up options are bound from, under its own name.</summary>
    public const string SectionName = "Bsft:Outbox:Postgres:Instances";

    /// <summary>
    /// Gets or sets the PostgreSQL notification channel the outbox table is announced on. It has no
    /// default: the writer, a trigger or a <c>pg_notify</c> call of the host, names it, and a listener
    /// that guessed would wake for another table's writes or none. It is restricted to lower-case
    /// letters, digits and underscores because it is quoted into a <c>LISTEN</c> statement, which takes
    /// an identifier and not a parameter.
    /// </summary>
    public string NotificationChannel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection the listener opens. It has no default for the same reason as the
    /// channel. The listener takes its connection from the pool of this connection string and holds it
    /// for the host's lifetime, idle almost all of that time, so it occupies one pool slot. A deployment
    /// that sizes its pool for request traffic gives the listener a different connection string (a
    /// second pool) or <c>Pooling=false</c>. It has to reach the primary: a standby does not deliver notifications.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long the listener waits before it reconnects after its connection ended.
    /// Defaults to five seconds. It has to be positive: reconnecting without a pause turns a server
    /// that is restarting into a connection storm.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = DefaultReconnectDelay;
}
