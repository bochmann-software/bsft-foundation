namespace Bsft.Idempotency.Tests.Support;

/// <summary>
/// Counts how often the endpoint behind the middleware actually ran, and lets a test hold one
/// invocation open so a second request meets a standing reservation rather than a race.
/// </summary>
internal sealed class HandlerProbe
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _invocations;

    /// <summary>Gets how often the endpoint ran.</summary>
    public int Invocations => Volatile.Read(ref _invocations);

    /// <summary>Gets a task that completes as soon as the endpoint has been entered once.</summary>
    public Task Entered => _entered.Task;

    /// <summary>
    /// Gets or sets a value indicating whether the next invocation waits for <see cref="Release"/>.
    /// Read once on entry, so clearing it while an invocation is held only affects the ones after it.
    /// </summary>
    public bool Blocking { get; set; }

    /// <summary>Lets a held invocation finish.</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>Counts one invocation and waits if the test asked for it.</summary>
    /// <returns>The number of this invocation, counting from one.</returns>
    public async Task<int> EnterAsync()
    {
        var invocation = Interlocked.Increment(ref _invocations);
        var blocking = Blocking;

        _entered.TrySetResult();

        if (blocking)
            await _released.Task;

        return invocation;
    }
}
