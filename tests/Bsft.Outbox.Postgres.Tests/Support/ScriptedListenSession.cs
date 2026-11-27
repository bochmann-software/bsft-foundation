namespace Bsft.Outbox.Postgres.Tests.Support;

/// <summary>
/// A listening session under the test's control: each one calls back as often as the script says, then
/// ends the way the script says, cleanly or by failing the way a dropped connection does.
/// </summary>
public sealed class ScriptedListenSession : IPostgresListenSession
{
    private readonly Func<int, Func<CancellationToken, Task>, CancellationToken, Task> _script;
    private readonly TaskCompletionSource _firstSession = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initializes a new instance of the <see cref="ScriptedListenSession"/> class.</summary>
    /// <param name="script">Given the 1-based session number, the callback and the token, runs the session.</param>
    public ScriptedListenSession(Func<int, Func<CancellationToken, Task>, CancellationToken, Task> script) => _script = script;

    /// <summary>Gets how many sessions were started.</summary>
    public int Sessions { get; private set; }

    /// <summary>Gets what the most recent session was asked to listen on.</summary>
    public (string ConnectionString, string Channel)? Last { get; private set; }

    /// <summary>Gets a task that completes when the first session starts.</summary>
    public Task FirstSessionStarted => _firstSession.Task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <inheritdoc />
    public Task ListenAsync(
        string connectionString,
        PostgresNotificationChannel channel,
        Func<CancellationToken, Task> onNotified,
        CancellationToken cancellationToken = default)
    {
        Sessions++;
        Last = (connectionString, channel.Name);
        _firstSession.TrySetResult();

        return _script(Sessions, onNotified, cancellationToken);
    }
}
