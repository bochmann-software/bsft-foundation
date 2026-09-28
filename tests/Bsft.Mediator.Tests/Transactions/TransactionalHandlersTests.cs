using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// Which handlers make a request transactional. The scan over the loaded assemblies is the whole
/// decision, so each case is a request type of its own (the answer is cached per request type) and
/// the outcome is read where a caller sees it: a write inside the dispatch that is either rolled back
/// with the failure or left standing.
/// </summary>
public class TransactionalHandlersTests
{
    public static TheoryData<object, bool> Requests => new()
    {
        // A concrete handler marked [Transactional], through each handler shape.
        { new TransactionalScanQuery(), true },
        { new TransactionalScanVoidCommand(), true },
        // The marker sits on a handler that also implements an unrelated interface.
        { new DisposableTransactionalCommand("scan"), true },
        // A handler without the marker, even though other handlers are marked.
        { new UnmarkedScanCommand(), false },
        // The marker on an abstract handler: the concrete handler deriving from it does not inherit it.
        { new AbstractlyMarkedScanCommand(), false },
        // The marker on a type implementing a generic contract over the request that is no handler.
        { new ForeignContractScanCommand(), false },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task HandleAsync_ByHowTheRequestIsHandled_OpensATransactionOnlyForAMarkedConcreteHandler(
        object request, bool transactional)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Probe(Id INTEGER)");

        var behavior = new TransactionBehavior<object, string>(
            provider, provider.GetRequiredService<ILogger<TransactionBehavior<object, string>>>());

        var handling = () => behavior.HandleAsync(
            request,
            async () =>
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (1)");
                throw new InvalidOperationException("simulated failure");
            });

        await handling.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated failure");

        Rows(connection).Should().Be(
            transactional ? 0 : 1,
            transactional
                ? "a marked concrete handler puts the dispatch in a transaction the failure rolls back"
                : "without a marked concrete handler the write is not wrapped and stays");
    }

    private static long Rows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Probe";

        return (long)command.ExecuteScalar()!;
    }
}
