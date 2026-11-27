namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>
/// A tenant-keyed outbox row that simulates a row source implementing <see cref="IOutboxRowScope{TEntity}"/>
/// to narrow queries to one tenant at a time.
/// </summary>
public sealed class TenantAwareIntentRow : OutboxRow
{
    /// <summary>Gets or sets the tenant the intent is for.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the intent payload.</summary>
    public string IntentName { get; set; } = string.Empty;
}
