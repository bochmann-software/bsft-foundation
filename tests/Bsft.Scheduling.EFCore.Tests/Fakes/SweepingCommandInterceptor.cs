using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Scheduling.EFCore.Tests.Fakes;

/// <summary>
/// Runs a statement on the context's own connection just before its n-th query executes, which
/// stands in for a concurrent sweep that removes rows between two reads of one pass.
/// </summary>
internal sealed class SweepingCommandInterceptor : DbCommandInterceptor
{
    private readonly int _beforeQuery;
    private readonly string _sweepSql;
    private int _queries;

    public SweepingCommandInterceptor(int beforeQuery, string sweepSql)
    {
        _beforeQuery = beforeQuery;
        _sweepSql = sweepSql;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _queries) == _beforeQuery)
        {
            using var sweep = command.Connection!.CreateCommand();
            sweep.CommandText = _sweepSql;
            await sweep.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }
}
