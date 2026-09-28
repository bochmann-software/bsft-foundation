using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// The declaration a caller makes when a save is deliberately a cross-tenant write, so that
/// <see cref="TenantSaveChangesInterceptor"/> lets a tenant-unchanged update through from a scope
/// where no tenant is resolved.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The guard denies by default.</strong> A modified <see cref="ITenantEntity"/> saved from a
/// tenant-less scope is refused unless the code doing the save has opened a declaration around it.
/// Nothing infers the intent: the declaration is written down at the call site, next to the reason it
/// is allowed, and it lasts exactly as long as the returned handle is not disposed.
/// </para>
/// <para>
/// It has to be an explicit declaration because the entity's own values cannot carry that intent.
/// Comparing the tracked <c>TenantId</c> against its <c>OriginalValue</c> looks like a check but is
/// not one on its own: for an entity that was never loaded through this context - <c>DbSet.Update</c>,
/// or <c>Attach</c> followed by <c>EntityState.Modified</c>, which is how several stores in this
/// framework write - EF Core seeds the original values from the current ones. The two are then equal
/// by construction, whatever tenant the caller put on the entity, and the comparison would wave every
/// such write through from a scope that has no tenant at all. Inside a declaration the comparison is
/// still made, as a second lock on a path where the values do mean something, but what authorises the
/// write is the declaration.
/// </para>
/// <para>
/// The declaration is scoped to one <see cref="DbContext"/> and tracked outside it, in a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, so no context has to carry a field for it and a
/// context that is collected takes its entry with it. Declarations nest: an inner one keeps the outer
/// one intact until both handles are disposed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using (context.AllowTenantUnchangedWrites())
/// {
///     await context.SaveChangesAsync(ct);
/// }
/// </code>
/// </example>
public static class TenantGuardScope
{
    private static readonly ConditionalWeakTable<DbContext, Declaration> Declarations = new();

    /// <summary>
    /// Declares that the saves made on the given context until the returned handle is disposed are
    /// cross-tenant writes, and may therefore update an <see cref="ITenantEntity"/> from a scope
    /// where no tenant is resolved as long as the entity stays with the tenant that owns it.
    /// </summary>
    /// <param name="context">The context the declaration applies to.</param>
    /// <returns>The handle that ends the declaration when it is disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public static IDisposable AllowTenantUnchangedWrites(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new DeclarationHandle(Declarations.GetValue(context, _ => new Declaration()));
    }

    /// <summary>
    /// Reports whether a cross-tenant write is declared on the given context right now.
    /// </summary>
    /// <param name="context">The context the save is running on.</param>
    /// <returns><see langword="true"/> when a declaration is open; otherwise <see langword="false"/>.</returns>
    internal static bool IsTenantUnchangedWriteAllowed(DbContext context) =>
        Declarations.TryGetValue(context, out var declaration) && declaration.IsOpen;

    /// <summary>The number of declarations open on one context.</summary>
    private sealed class Declaration
    {
        private int _depth;

        /// <summary>Gets a value indicating whether at least one declaration is open.</summary>
        public bool IsOpen => Volatile.Read(ref _depth) > 0;

        /// <summary>Opens one declaration.</summary>
        public void Open() => Interlocked.Increment(ref _depth);

        /// <summary>Closes one declaration.</summary>
        public void Close() => Interlocked.Decrement(ref _depth);
    }

    /// <summary>
    /// One caller's declaration. Disposing it twice closes the declaration once, so that a
    /// <c>using</c> inside a method that also disposes explicitly cannot reopen somebody else's.
    /// </summary>
    private sealed class DeclarationHandle : IDisposable
    {
        private readonly Declaration _declaration;
        private bool _closed;

        /// <summary>Initializes a new instance of the <see cref="DeclarationHandle"/> class.</summary>
        /// <param name="declaration">The declaration of the context this handle belongs to.</param>
        public DeclarationHandle(Declaration declaration)
        {
            _declaration = declaration;
            _declaration.Open();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_closed)
                return;

            _closed = true;
            _declaration.Close();
        }
    }
}
