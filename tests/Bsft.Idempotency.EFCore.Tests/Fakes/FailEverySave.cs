using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// Fails every save the way an unreachable database does: with a <see cref="DbUpdateException"/> that
/// is not a unique violation and leaves no row behind.
/// </summary>
internal sealed class FailEverySave : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        throw new DbUpdateException("The database is unreachable.");
}
