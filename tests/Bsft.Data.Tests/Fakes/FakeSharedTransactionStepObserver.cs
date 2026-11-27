using Bsft.Data.Transactions;

namespace Bsft.Data.Tests.Fakes;

/// <summary>
/// Writes every notification into a log shared by the observers of a test, as <c>+Kind</c> for a starting
/// and <c>-Kind</c> for a completed step, prefixed with its name; refuses the steps it is told to and can
/// fail every completion.
/// </summary>
public sealed class FakeSharedTransactionStepObserver(List<string> log, string name = "") : ISharedTransactionStepObserver
{
    /// <summary>The exception <see cref="OnStepStarting"/> throws for a step kind.</summary>
    public Dictionary<SharedTransactionStepKind, Exception> Refusals { get; } = [];

    /// <summary>The exception <see cref="OnStepCompleted"/> throws, if any.</summary>
    public Exception? CompletionFailure { get; set; }

    /// <summary>Every step this observer was told about as starting.</summary>
    public List<SharedTransactionStepInfo> Steps { get; } = [];

    public void OnStepStarting(SharedTransactionStepInfo step)
    {
        log.Add($"{name}+{step.Kind}");
        Steps.Add(step);

        if (Refusals.TryGetValue(step.Kind, out var refusal))
            throw refusal;
    }

    public void OnStepCompleted(SharedTransactionStepInfo step)
    {
        log.Add($"{name}-{step.Kind}");

        if (CompletionFailure is { } failure)
            throw failure;
    }
}
