using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Scheduling.EFCore.Tests.Fakes;

/// <summary>
/// Records the text of every command a context runs, so a test can assert how many statements a
/// pass issued and what shape they had.
/// </summary>
internal sealed class RecordingCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyList<string> Commands => _commands.ToArray();

    public void Clear() => _commands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
