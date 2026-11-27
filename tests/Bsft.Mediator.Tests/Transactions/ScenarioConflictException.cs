namespace Bsft.Mediator.Tests.Transactions;

/// <summary>Stands in for a serialization failure the store's provider classifies as transient.</summary>
public sealed class ScenarioConflictException : Exception
{
    public ScenarioConflictException()
        : base("could not serialize access due to concurrent update")
    {
    }
}
