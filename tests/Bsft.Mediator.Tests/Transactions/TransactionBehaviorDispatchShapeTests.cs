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
/// The behavior as the dispatcher actually closes it: over the declared contract, not over the
/// concrete command. Reading <c>[Transactional]</c> off the type parameter found no handler there,
/// so a marked handler ran with no transaction at all on the one path that matters.
/// </summary>
public class TransactionBehaviorDispatchShapeTests
{
    [Fact]
    public async Task HandleAsync_ClosedOverTheCommandContract_StillOpensTheTransaction()
    {
        await RollsBackAsync<ICommand<string>, string>(
            new TransactionalTestCommand("tx"),
            services => services
                .AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>());
    }

    [Fact]
    public async Task HandleAsync_ClosedOverTheResultlessCommandContract_StillOpensTheTransaction()
    {
        await RollsBackAsync<ICommand<Unit>, Unit>(
            new TransactionalVoidCommand("tx"),
            services => services
                .AddTransient<ICommandHandler<TransactionalVoidCommand>, TransactionalVoidCommandHandler>());
    }

    /// <summary>
    /// Writes a row inside <c>next</c> and then throws. The row is only gone afterwards if the
    /// behavior opened a transaction for this request, which is the whole assertion.
    /// </summary>
    private static async Task RollsBackAsync<TRequest, TResponse>(
        TRequest request, Action<ServiceCollection> registerHandler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        registerHandler(services);

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Probe(Id INTEGER)");

        var behavior = new TransactionBehavior<TRequest, TResponse>(
            provider, provider.GetRequiredService<ILogger<TransactionBehavior<TRequest, TResponse>>>());

        var handling = () => behavior.HandleAsync(
            request,
            async () =>
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (1)");
                throw new InvalidOperationException("simulated failure");
            });

        await handling.Should().ThrowAsync<InvalidOperationException>();

        Rows(connection).Should().Be(0);
    }

    private static long Rows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Probe";

        return (long)command.ExecuteScalar()!;
    }
}
