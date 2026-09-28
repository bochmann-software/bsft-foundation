namespace Bsft.Outbox.Tests.Support;

/// <summary>A side effect a test decides the outcome of, payload by payload.</summary>
public sealed class ScriptedSideEffect : IOutboxSideEffect<string>
{
    /// <summary>What each payload does when it is applied. An absent payload succeeds.</summary>
    public Dictionary<string, Func<Task>> Outcomes { get; } = [];

    /// <summary>The payloads applied, in order.</summary>
    public List<string> Applied { get; } = [];

    /// <inheritdoc />
    public Task ApplyAsync(string payload, CancellationToken ct = default)
    {
        Applied.Add(payload);

        return Outcomes.TryGetValue(payload, out var outcome) ? outcome() : Task.CompletedTask;
    }

    /// <inheritdoc />
    public string Describe(string payload) => $"payload {payload}";
}
