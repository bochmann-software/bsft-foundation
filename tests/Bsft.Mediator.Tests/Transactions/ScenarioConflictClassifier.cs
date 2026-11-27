using Bsft.Data.Transactions;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>The provider classification of the scenarios: only <see cref="ScenarioConflictException"/> is a conflict.</summary>
public sealed class ScenarioConflictClassifier : ITransactionConflictClassifier
{
    public bool IsConflict(Exception exception) => exception is ScenarioConflictException;
}
