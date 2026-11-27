using Bsft.Data.MultiTenancy;
using Bsft.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Messaging.EFCore.Outbox;

/// <summary>
/// Where the integration-event outbox's rows live and how one is read: the two things the shared
/// lease protocol asks this package for.
/// </summary>
/// <remarks>
/// The set comes from the application's own <see cref="IOutboxDbContext"/>, which is the whole point
/// of keeping the outbox in that context: the state change and the event it raises are written by one
/// <c>SaveChanges</c> and cannot disagree.
/// </remarks>
internal sealed class OutboxMessageRowSource
    : IOutboxRowSource<OutboxMessageEntity, IntegrationEventMessage>, IOutboxRowScope<OutboxMessageEntity>
{
    /// <inheritdoc />
    public int FailureReasonMaxLength => OutboxMessageEntityConfiguration.FailureReasonMaxLength;

    /// <inheritdoc />
    public DbSet<OutboxMessageEntity> Rows(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        return scopedServices.GetRequiredService<IOutboxDbContext>().OutboxMessages;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nothing is re-encoded: the payload is the one the row was written with, so what a consumer
    /// reads is what the transaction committed.
    /// </remarks>
    public IntegrationEventMessage PayloadOf(OutboxMessageEntity row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new IntegrationEventMessage
        {
            EventId = row.EventId,
            EventName = row.EventTypeName,
            OccurredAt = new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)),
            CorrelationId = row.CorrelationId,
            Payload = row.PayloadJson,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Under tenant keys a pass inside a <c>BackgroundTenant</c> polls and settles that tenant's rows
    /// only, so every statement of the lease protocol carries both halves of the key. A pass without a
    /// tenant sees every row; its claim is held to the tenant of the row it claims
    /// (<see cref="WithOwnerOf"/>), and its settling writes match the lease token, which no row of another
    /// tenant can share.
    /// </remarks>
    public IQueryable<OutboxMessageEntity> InScope(IQueryable<OutboxMessageEntity> rows, IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(scopedServices);

        var set = scopedServices.GetRequiredService<IOutboxDbContext>().OutboxMessages;

        if (!set.IsTenantKeyed() || scopedServices.FindCurrentTenantId() is not { } tenantId)
            return rows;

        return rows.Where(row => row.TenantId == tenantId);
    }

    /// <inheritdoc />
    public IQueryable<OutboxMessageEntity> WithOwnerOf(IQueryable<OutboxMessageEntity> rows, OutboxMessageEntity row)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(row);

        // The tenant is set on a row read from a tenant-keyed table only; without tenant keys the
        // column does not exist and the id alone is the key.
        if (row.TenantId is not { } tenantId)
            return rows;

        return rows.Where(candidate => candidate.TenantId == tenantId);
    }
}
