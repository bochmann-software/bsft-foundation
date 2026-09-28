using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>
/// Stands in for a second application instance: once, right after the outbox poll has read its
/// candidates, it abandons the message the way a failed delivery elsewhere would — raising the
/// attempt count while leaving the lease token alone. That is the exact interleaving the claim has to
/// notice.
/// </summary>
public sealed class AttemptBumpingInterceptor : DbCommandInterceptor
{
    private readonly SqliteConnection _connection;
    private bool _hasBumped;

    public AttemptBumpingInterceptor(SqliteConnection connection) => _connection = connection;

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Bump(command);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Bump(command);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void Bump(DbCommand command)
    {
        if (_hasBumped || !command.CommandText.Contains("OutboxMessages", StringComparison.Ordinal))
            return;

        _hasBumped = true;

        using var update = _connection.CreateCommand();
        update.CommandText = "UPDATE OutboxMessages SET AttemptCount = AttemptCount + 1";
        update.ExecuteNonQuery();
    }
}
