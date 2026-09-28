using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Idempotency.EFCore.Tests.Fakes;

/// <summary>
/// Stands in for a request arriving while a sweep is halfway through: once, between the sweep's read
/// of the expired ids and the delete that removes them, it runs a competing statement through another
/// connection.
/// </summary>
internal sealed class RunBeforeTheDelete : DbCommandInterceptor
{
    private readonly Func<Task> _competitor;
    private bool _fired;

    public RunBeforeTheDelete(Func<Task> competitor) => _competitor = competitor;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_fired && command.CommandText.Contains("DELETE", StringComparison.Ordinal))
        {
            _fired = true;
            await _competitor().ConfigureAwait(false);
        }

        return await base
            .NonQueryExecutingAsync(command, eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }
}
