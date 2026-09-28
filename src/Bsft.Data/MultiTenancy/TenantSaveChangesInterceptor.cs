using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// EF Core interceptor that automatically sets the tenant identifier on new
/// <see cref="ITenantEntity"/> instances and validates tenant consistency on updates.
/// </summary>
/// <remarks>
/// <para>
/// An insert always needs a resolved tenant, because the row would otherwise be written with a null
/// <see cref="ITenantEntity.TenantId"/> and belong to nobody. An update is checked against the
/// ambient tenant whenever there is one, so no request can write another tenant's row.
/// </para>
/// <para>
/// <strong>A modified entity saved from a tenant-less scope is refused by default.</strong> The one
/// allowance is for a caller that has declared the save a cross-tenant write with
/// <see cref="TenantGuardScope.AllowTenantUnchangedWrites"/>, and even then only while the entity's
/// own <see cref="ITenantEntity.TenantId"/> is unchanged from the value it was loaded with. That is
/// the shape a background sweep has: a scheduler job or a retention pruner reads rows across tenants,
/// writes each one back where it found it, and has no ambient tenant to be compared against.
/// </para>
/// <para>
/// The declaration is what authorises that path, and the comparison against the original value is a
/// second lock rather than the first one. On its own the comparison would guard nothing: for an
/// entity that was never loaded through this context - <c>DbSet.Update</c>, or <c>Attach</c> plus
/// <see cref="EntityState.Modified"/> - EF Core seeds the original values from the current ones, so
/// original and current are equal whatever tenant the caller put on the entity. Undeclared, such a
/// write is refused; declared, it still has to leave the row with the tenant it was read under, so
/// moving a row to another tenant remains the write the ambient tenant would have caught.
/// </para>
/// </remarks>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>What a modified tenant-scoped entity is refused with when nothing was declared.</summary>
    private const string Undeclared =
        "Cannot persist a tenant-scoped entity because no tenant is resolved in the current context. "
        + "A background job that writes rows back where it found them must declare the save with "
        + "TenantGuardScope.AllowTenantUnchangedWrites.";

    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantSaveChangesInterceptor"/> class.
    /// </summary>
    /// <param name="tenantContext">The current tenant context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantContext"/> is null.</exception>
    public TenantSaveChangesInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyTenantRules(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantRules(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyTenantRules(DbContext? context)
    {
        if (context is null)
            return;

        var entries = context.ChangeTracker.Entries<ITenantEntity>();

        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Added)
                StampAdded(entry);
            else if (entry.State is EntityState.Modified)
                CheckModified(entry, context);
        }
    }

    /// <summary>
    /// Stamps a new entity with the ambient tenant, and refuses the insert when there is none:
    /// the row would otherwise be persisted with a null tenant and belong to nobody.
    /// </summary>
    /// <param name="entry">The entry of the entity being inserted.</param>
    /// <exception cref="InvalidOperationException">Thrown when no tenant is resolved.</exception>
    private void StampAdded(EntityEntry<ITenantEntity> entry)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException(
                "Cannot persist a tenant-scoped entity because no tenant is resolved in the current context.");

        entry.Entity.TenantId = _tenantContext.TenantId;
    }

    /// <summary>
    /// Checks an update against the ambient tenant, or - when there is none and the caller has
    /// declared the save a cross-tenant write - against the tenant the entity was loaded with, so
    /// that a background sweep may write a row back where it found it.
    /// </summary>
    /// <param name="entry">The entry of the entity being updated.</param>
    /// <param name="context">The context the save is running on, which a declaration is looked up on.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the entity belongs to another tenant than the ambient one, when no tenant is
    /// resolved and no cross-tenant write is declared, or when a declared write moves the entity to a
    /// different tenant.
    /// </exception>
    /// <remarks>
    /// The declaration is looked up inside the tenant-less branch rather than passed in already
    /// resolved: it is consulted nowhere else, and an ordinary tenant-resolved save should not pay for
    /// a lookup whose result it never reads.
    /// </remarks>
    private void CheckModified(EntityEntry<ITenantEntity> entry, DbContext context)
    {
        var current = entry.Entity.TenantId;

        if (_tenantContext.TenantId is null)
        {
            CheckTenantLessUpdate(entry, current, context);
            return;
        }

        if (!string.Equals(current, _tenantContext.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cross-tenant write detected. Entity tenant '{current}' " +
                $"does not match current tenant '{_tenantContext.TenantId}'.");
    }

    /// <summary>
    /// Checks an update made where no tenant is resolved: refused unless the caller declared the save
    /// a cross-tenant write, and then only while the entity stays with the tenant it was loaded under.
    /// </summary>
    /// <param name="entry">The entry of the entity being updated.</param>
    /// <param name="current">The tenant the entity carries now.</param>
    /// <param name="context">The context the save is running on, which the declaration is read from.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no cross-tenant write is declared, or when a declared write moves the entity to a
    /// different tenant.
    /// </exception>
    private static void CheckTenantLessUpdate(
        EntityEntry<ITenantEntity> entry,
        string? current,
        DbContext context)
    {
        // There is no ambient tenant to hold the row against, and the entity's own values cannot
        // stand in for one: EF Core seeds the original values from the current ones for anything
        // attached without having been loaded here, so an unchanged TenantId would prove nothing
        // about where the row came from. Only a caller that has written the declaration down may
        // take this path at all.
        if (!TenantGuardScope.IsTenantUnchangedWriteAllowed(context))
            throw new InvalidOperationException(Undeclared);

        // Inside the declaration the row is compared against itself, as a second lock: what was
        // read under one tenant is written back under it, and a write that moves the row to
        // another tenant is the one the ambient tenant would have caught.
        var original = entry.Property(nameof(ITenantEntity.TenantId)).OriginalValue as string;

        if (!string.Equals(original, current, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cannot move a tenant-scoped entity from tenant '{original}' to '{current}' " +
                "because no tenant is resolved in the current context.");
    }
}
