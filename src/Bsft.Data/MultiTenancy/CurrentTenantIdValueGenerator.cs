using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Fills the <c>TenantId</c> of a tenant-keyed row as the row is added, with the tenant of the scope
/// the context belongs to.
/// </summary>
/// <remarks>
/// A writer that knows the row's tenant better, an audit event that carries its own, sets it and this
/// generator is not asked. A scope without a tenant fails the add instead of writing a row nobody owns.
/// </remarks>
internal sealed class CurrentTenantIdValueGenerator : ValueGenerator<string>
{
    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override string Next(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return TenantKeyServices.FindApplicationServices(entry.Context)
            .GetCurrentTenantId(entry.Metadata.GetTableName() ?? entry.Metadata.DisplayName());
    }
}
