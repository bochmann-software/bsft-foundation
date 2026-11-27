using Bsft.Data.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>
/// A row source that implements <see cref="IOutboxRowScope{TEntity}"/> to demonstrate tenant filtering
/// in the lease protocol.
/// </summary>
public sealed class TenantAwareIntentRowSource
    : IOutboxRowSource<TenantAwareIntentRow, string>, IOutboxRowScope<TenantAwareIntentRow>
{
    /// <summary>The width of the recorded failure, which is what a reason is cut to.</summary>
    public const int ReasonMaxLength = 32;

    /// <inheritdoc />
    public int FailureReasonMaxLength => ReasonMaxLength;

    /// <inheritdoc />
    public DbSet<TenantAwareIntentRow> Rows(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        return scopedServices.GetRequiredService<TenantAwareDbContext>().Intents;
    }

    /// <inheritdoc />
    public string PayloadOf(TenantAwareIntentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.IntentName;
    }

    /// <inheritdoc />
    public IQueryable<TenantAwareIntentRow> InScope(IQueryable<TenantAwareIntentRow> rows, IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(scopedServices);

        var tenantContext = scopedServices.GetService<ITenantContext>();
        var currentTenant = tenantContext?.TenantId;

        if (string.IsNullOrEmpty(currentTenant))
            return rows;

        return rows.Where(row => row.TenantId == currentTenant);
    }

    /// <inheritdoc />
    public IQueryable<TenantAwareIntentRow> WithOwnerOf(IQueryable<TenantAwareIntentRow> rows, TenantAwareIntentRow row)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(row);

        return rows.Where(candidate => candidate.TenantId == row.TenantId);
    }
}
