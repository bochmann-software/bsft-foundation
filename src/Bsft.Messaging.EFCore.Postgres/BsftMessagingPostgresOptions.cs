namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// Options controlling the PostgreSQL wake-up of the EF Core outbox: which channel the commit is
/// announced on, which connection listens for it, and how long the listener waits before it comes
/// back after the connection dropped.
/// </summary>
/// <remarks>
/// Nothing here changes what is delivered or how often it may be retried — that stays with
/// <c>Bsft:Messaging:Outbox</c>. This package only shortens the wait between a commit and the drain
/// that follows it, and the poll interval configured there remains the guarantee whenever the
/// notification does not arrive.
/// </remarks>
public sealed class BsftMessagingPostgresOptions
{
    /// <summary>The configuration section this options type is bound from.</summary>
    public const string SectionName = "Bsft:Messaging:Postgres";

    /// <summary>
    /// Gets or sets the PostgreSQL notification channel the outbox is announced on. Defaults to
    /// <see cref="PostgresDefaults.NotificationChannel"/>. It is restricted to lower-case letters,
    /// digits and underscores because it is quoted into a <c>LISTEN</c> statement, which takes an
    /// identifier and not a parameter.
    /// </summary>
    public string NotificationChannel { get; set; } = PostgresDefaults.NotificationChannel;

    /// <summary>
    /// Gets or sets the connection the listener opens. Defaults to <see langword="null"/>, which means
    /// the connection string of the application's own <c>DbContext</c> is used.
    /// </summary>
    /// <remarks>
    /// A listening connection is held open for as long as the application runs and is idle almost all
    /// of that time, so a deployment that sizes its pool for request traffic may prefer to give the
    /// listener a connection string of its own — a second pool, a replica, or the same server with
    /// pooling switched off. That is what this exists for; leaving it unset is right for most hosts.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets whether a save wakes this process's dispatcher directly instead of announcing itself
    /// through <c>pg_notify</c>. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Switch it on when the tenants live in databases of their own (silo mode): a <c>LISTEN</c> session on
    /// one connection hears one database, so it would wake the dispatcher for one tenant only. With this on,
    /// no listening connection is opened, <see cref="NotificationChannel"/> and <see cref="ConnectionString"/>
    /// go unused, and the poll interval stays the guarantee for rows another instance committed.
    /// </remarks>
    public bool IsInProcessWake { get; set; }

    /// <summary>
    /// Gets or sets how long the listener waits before it reconnects after its connection ended.
    /// Defaults to five seconds. It has to be positive: reconnecting without a pause turns a server
    /// that is restarting into a connection storm.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
}
