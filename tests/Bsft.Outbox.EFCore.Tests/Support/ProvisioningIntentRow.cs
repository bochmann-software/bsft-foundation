namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>
/// A second outbox's row, unrelated to integration events: the intent to provision a tenant, committed
/// in the same transaction as the tenant itself and carried out afterwards.
/// </summary>
/// <remarks>
/// It exists to hold the arrangement to its claim. The lifecycle is inherited and nothing about it is
/// restated here, so an outbox that is not a message queue costs one entity and one row source.
/// </remarks>
public sealed class ProvisioningIntentRow : OutboxRow
{
    /// <summary>Gets or sets the tenant the intent is about.</summary>
    public string TenantId { get; set; } = string.Empty;
}
