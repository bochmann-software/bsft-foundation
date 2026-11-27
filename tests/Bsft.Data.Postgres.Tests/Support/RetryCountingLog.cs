namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Tracks retry attempt counts and context identity for test assertions.</summary>
public sealed class RetryCountingLog
{
    public int CommandAttempts { get; set; }

    public string FailureMode { get; set; } = "serialization";

    public int FailuresBeforeSuccess { get; set; }

    public int FirstAttemptContextId { get; set; }

    public bool HasFreshChangeTracker { get; set; }
}
