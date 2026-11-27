using System.Data.Common;
using Bsft.Data.Transactions;
using Npgsql;

namespace Bsft.Data.Postgres;

/// <summary>
/// Asks PostgreSQL whether it already aborted the shared transaction, by running <c>SELECT 1</c> in it:
/// an aborted transaction answers every statement with <c>25P02</c> (in failed SQL transaction).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL turns the <c>COMMIT</c> of an aborted transaction into a <c>ROLLBACK</c> and Npgsql reports
/// no error for it, so without the question a unit whose handler swallowed a failure raised while rows
/// were read would report success for writes that were never persisted.
/// </para>
/// <para>
/// The statement is run on the connection directly rather than through EF Core, so no interceptor sees
/// it. It costs one round trip per committed unit: measured at 0.32 to 0.36 ms against PostgreSQL 16
/// in a local container (5000 probes, three rounds), next to about 7 ms for the insert and commit of a
/// one-row unit on the same machine. Over a network it costs one network round trip.
/// </para>
/// </remarks>
internal sealed class PostgresTransactionAbortProbe : ITransactionAbortProbe
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transaction"/> is null.</exception>
    public async Task<Exception?> FindAbortAsync(DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var connection = transaction.Connection
            ?? throw new InvalidOperationException("The transaction has already completed, so there is nothing to probe.");
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";

        try
        {
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InFailedSqlTransaction)
        {
            return exception;
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
        }
    }
}
