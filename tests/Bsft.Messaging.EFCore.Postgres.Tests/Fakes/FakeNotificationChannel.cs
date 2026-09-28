namespace Bsft.Messaging.EFCore.Postgres.Tests.Fakes;

/// <summary>
/// A listening session under the test's control: it hands the worker as many notifications as the
/// script says, then ends the way the script says it ends — cleanly, or by failing the way a dropped
/// connection does.
/// </summary>
public sealed class FakeNotificationChannel : IOutboxNotificationChannel
{
    private readonly int _notificationsPerSession;
    private readonly Func<int, Exception?> _endOfSession;
    private readonly TaskCompletionSource _firstSession = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initializes a new instance of the <see cref="FakeNotificationChannel"/> class.</summary>
    /// <param name="notificationsPerSession">How many times a session calls back before it ends.</param>
    /// <param name="endOfSession">
    /// Given the number of sessions started so far, the failure that ends this one, or
    /// <see langword="null"/> for a session that simply returns.
    /// </param>
    public FakeNotificationChannel(int notificationsPerSession = 1, Func<int, Exception?>? endOfSession = null)
    {
        _notificationsPerSession = notificationsPerSession;
        _endOfSession = endOfSession ?? (_ => null);
    }

    /// <summary>Gets how many sessions were started.</summary>
    public int Sessions { get; private set; }

    /// <summary>Gets a task that completes when the first session starts.</summary>
    public Task FirstSessionStarted => _firstSession.Task;

    /// <inheritdoc />
    public async Task ListenAsync(Func<CancellationToken, Task> onNotified, CancellationToken ct = default)
    {
        if (onNotified is null)
            throw new ArgumentNullException(nameof(onNotified));

        Sessions++;
        _firstSession.TrySetResult();

        for (var notification = 0; notification < _notificationsPerSession; notification++)
        {
            ct.ThrowIfCancellationRequested();

            await onNotified(ct).ConfigureAwait(false);
        }

        if (_endOfSession(Sessions) is { } failure)
            throw failure;
    }
}
