using Bsft.Mediator;

namespace Bsft.Resilience.Mediator.Tests.Fakes;

/// <summary>The scoped marks the mediator keeps, flowing like the real ones, with a count of the retry marks.</summary>
public sealed class FakeRequestNesting : IRequestNesting
{
    private readonly AsyncLocal<bool> _retrying = new();
    private readonly AsyncLocal<bool> _transactionOpen = new();

    public bool IsRetrying => _retrying.Value;

    public bool IsTransactionOpen => _transactionOpen.Value;

    public int RetryMarks { get; private set; }

    public void MarkRetrying()
    {
        RetryMarks++;
        _retrying.Value = true;
    }

    public void MarkTransactionOpen() => _transactionOpen.Value = true;
}
