using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// Stands in for a second replica: once, in the window between the read of a lapsed record and the
/// update that reclaims it, it runs a competing statement through another connection.
/// </summary>
internal sealed class RunBeforeTheUpdate : DbCommandInterceptor
{
    private readonly Func<Task> _competitor;
    private bool _fired;

    public RunBeforeTheUpdate(Func<Task> competitor) => _competitor = competitor;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_fired && command.CommandText.Contains("UPDATE", StringComparison.Ordinal))
        {
            _fired = true;
            await _competitor().ConfigureAwait(false);
        }

        return await base
            .NonQueryExecutingAsync(command, eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }
}
