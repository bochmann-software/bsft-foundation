namespace Bsft.Mediator.Tests.Transactions;

/// <summary>A row either module context of the shared-transaction scenarios writes.</summary>
public sealed class ScenarioRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
