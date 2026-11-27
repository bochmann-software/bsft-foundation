using Bsft.Mediator;

namespace Bsft.Resilience.Mediator.Tests.Fakes;

/// <summary>The mediator's transaction policy, as a test sets it.</summary>
public sealed class FakeMediatorTransactionPolicy : IMediatorTransactionPolicy
{
    public bool ShouldApplyTransactionToAllCommands { get; init; }
}
