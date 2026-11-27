namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Tracks retry attempts for tenant tests.</summary>
public sealed class TenantRetryLog
{
    public int CommandAttempts { get; set; }
}
