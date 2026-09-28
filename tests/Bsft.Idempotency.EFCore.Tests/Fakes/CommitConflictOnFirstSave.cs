using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// Commits a conflicting write through another connection the first time a context saves, and never
/// again, so a save deterministically meets a unique index it did not see when it read.
/// </summary>
internal sealed class CommitConflictOnFirstSave : SaveChangesInterceptor
{
    private readonly Func<Task> _inject;
    private bool _fired;

    public CommitConflictOnFirstSave(Func<Task> inject) => _inject = inject;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_fired)
        {
            _fired = true;
            await _inject().ConfigureAwait(false);
        }

        return await base
            .SavingChangesAsync(eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }
}
