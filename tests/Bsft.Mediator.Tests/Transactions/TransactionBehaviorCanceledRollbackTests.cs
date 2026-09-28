using Bsft.Core;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// A cancelled request still has to undo its partial write. Passing the caller's token into
/// <c>RollbackAsync</c> made EF Core short-circuit on it, so the rollback threw
/// <see cref="OperationCanceledException"/> over the handler's real exception and the undo was left
/// to dispose.
/// </summary>
public class TransactionBehaviorCanceledRollbackTests
{
    [Fact]
    public async Task HandleAsync_WhenTheRequestTokenIsAlreadyCancelled_StillRollsBackAndRethrowsTheHandlerException()
    {
        var services = new ServiceCollection();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Probe(Id INTEGER)");

        var logger = new RecordingLogger<TransactionBehavior<TransactionalTestCommand, string>>();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(provider, logger);

        using var cancellation = new CancellationTokenSource();

        var handling = () => behavior.HandleAsync(
            new TransactionalTestCommand("tx"),
            async () =>
            {
                // The write lands inside the transaction, then the request is aborted and the
                // handler fails - the shape of a client disconnect on the write path.
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (1)");
                await cancellation.CancelAsync();
                throw new InvalidOperationException("simulated failure");
            },
            cancellation.Token);

        await handling.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated failure");

        // The warning only runs when the rollback returned, so it proves the rollback was invoked
        // rather than the undo having been left to dispose.
        logger.Messages.Should().Contain(message => message.Contains("Rolled back transaction", StringComparison.Ordinal));
        Rows(connection).Should().Be(0);
    }

    private static long Rows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Probe";

        return (long)command.ExecuteScalar()!;
    }
}
