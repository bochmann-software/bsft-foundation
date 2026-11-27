namespace Bsft.Outbox.Tests.Support;

/// <summary>A drain pass whose outcome a test scripts, pass by pass, and whose calls it can wait for.</summary>
public sealed class ScriptedDrainer : IOutboxDrainer<string>
{
    private readonly Func<int, CancellationToken, Task<int>> _script;
    private readonly List<TaskCompletionSource> _started = [];
    private int _passes;

    /// <summary>Initializes a new instance of the <see cref="ScriptedDrainer"/> class.</summary>
    /// <param name="script">Given the 1-based pass number and the token the pass runs on, the rows it applied.</param>
    public ScriptedDrainer(Func<int, CancellationToken, Task<int>> script) => _script = script;

    /// <summary>Gets how many passes were started.</summary>
    public int Passes => Volatile.Read(ref _passes);

    /// <summary>Gets the token the most recent pass ran on.</summary>
    public CancellationToken LastToken { get; private set; }

    /// <summary>Completes when the given 1-based pass has started.</summary>
    /// <param name="pass">The pass number.</param>
    /// <returns>A task that completes when that pass starts.</returns>
    public Task StartedAsync(int pass)
    {
        lock (_started)
        {
            while (_started.Count < pass)
                _started.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            return _started[pass - 1].Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <inheritdoc />
    public Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var pass = Interlocked.Increment(ref _passes);
        LastToken = cancellationToken;

        lock (_started)
        {
            while (_started.Count < pass)
                _started.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            _started[pass - 1].TrySetResult();
        }

        return _script(pass, cancellationToken);
    }
}
