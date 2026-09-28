using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>Where the second outbox's rows live and how one is read.</summary>
public sealed class ProvisioningIntentRowSource : IOutboxRowSource<ProvisioningIntentRow, string>
{
    /// <summary>The width of the recorded failure, which is what a reason is cut to.</summary>
    public const int ReasonMaxLength = 32;

    /// <inheritdoc />
    public int FailureReasonMaxLength => ReasonMaxLength;

    /// <inheritdoc />
    public DbSet<ProvisioningIntentRow> Rows(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        return scopedServices.GetRequiredService<ProvisioningDbContext>().Intents;
    }

    /// <inheritdoc />
    public string PayloadOf(ProvisioningIntentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.TenantId;
    }
}
