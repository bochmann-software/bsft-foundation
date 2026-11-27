namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Counts the handler runs of one scenario and says which of them a concurrent writer races.</summary>
public sealed class ScenarioLog
{
    public string ConnectionString { get; set; } = string.Empty;

    public int OrderAttempts { get; set; }

    public int AdjustmentAttempts { get; set; }

    /// <summary>How many adjustment runs, counted from the first, a concurrent writer races.</summary>
    public int RacedAdjustments { get; set; }

    /// <summary>How many adjustment runs, counted from the first, hit a deadlock the handler swallows.</summary>
    public int SwallowedDeadlocks { get; set; }

    /// <summary>Whether a run that swallowed the deadlock goes on writing, into the aborted transaction.</summary>
    public bool IsWritingAfterTheSwallowedDeadlock { get; set; }

    /// <summary>How many adjustment runs, counted from the first, fail with an error that is no conflict.</summary>
    public int FailedAdjustments { get; set; }

    /// <summary>Whether the order handler catches the failure of its nested adjustment and completes anyway.</summary>
    public bool IsSwallowingTheAdjustmentFailure { get; set; }
}
