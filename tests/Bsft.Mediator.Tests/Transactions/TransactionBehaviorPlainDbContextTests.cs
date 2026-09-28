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
/// The registration contract: a plain <c>services.AddDbContext&lt;AppDbContext&gt;()</c> is enough for
/// <c>[Transactional]</c> to wrap the handler. <c>AddDbContext&lt;TContext&gt;</c> registers only the
/// concrete context, never the base <see cref="DbContext"/>, so resolving by base type alone returned
/// null and the handler ran unwrapped - a multi-step command silently partially committed on a
/// mid-way exception. The behavior now falls back to the concrete context named by the registered
/// <c>DbContextOptions</c>, so no <c>AddTransient&lt;DbContext&gt;(...)</c> line is required.
/// </summary>
public class TransactionBehaviorPlainDbContextTests
{
    [Fact]
    public async Task HandleAsync_WhenOnlyAddDbContextRegistered_RollsBackAllStepsOnMidHandlerException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        // A plain AddDbContext<T>() and nothing else: no base-DbContext registration.
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Probe(Id INTEGER)");

        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(
            provider, provider.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>());

        var handling = () => behavior.HandleAsync(
            new TransactionalTestCommand("tx"),
            async () =>
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (1)");
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (2)");
                throw new InvalidOperationException("simulated failure");
            });

        await handling.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated failure");

        // Both writes are gone only if the behavior actually opened a transaction for this request.
        Rows(connection).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenOnlyAddDbContextRegistered_CommitsAllStepsOnSuccess()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Probe(Id INTEGER)");

        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(
            provider, provider.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>());

        var result = await behavior.HandleAsync(
            new TransactionalTestCommand("tx"),
            async () =>
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (1)");
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (2)");
                return "committed";
            });

        result.Should().Be("committed");
        Rows(connection).Should().Be(2);
    }

    private static long Rows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Probe";

        return (long)command.ExecuteScalar()!;
    }
}
