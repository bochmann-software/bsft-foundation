using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Runs a statement on the shared connection directly, past EF Core: no interceptor sees it, so a failure it
/// raises reaches the shared transaction only as the exception of the unit that ran it.
/// </summary>
public static class RawSqlOnSharedConnection
{
    /// <summary>Raises a PostgreSQL error with <paramref name="sqlState"/> inside the open shared transaction.</summary>
    public static async Task RaiseAsync(DbContext context, string sqlState, CancellationToken cancellationToken)
    {
        // A query first, so the context has taken the shared transaction and the raw command can join it.
        await context.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"DO $$ BEGIN RAISE EXCEPTION 'raised past EF Core' USING ERRCODE = '{sqlState}'; END $$";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
