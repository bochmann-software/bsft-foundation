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
/// The multi-context registration contract. With more than one <c>AddDbContext&lt;T&gt;()</c> and no
/// explicit base <c>DbContext</c>, the non-generic <c>DbContextOptions</c> binds to whichever context
/// was registered first, so the fallback could open a transaction on the wrong context. A host that
/// registers the base <c>DbContext</c> explicitly disambiguates it, and the behavior wraps that one.
/// </summary>
public class TransactionBehaviorMultiContextTests
{
    [Fact]
    public async Task HandleAsync_WhenTwoContextsAndBaseDisambiguates_RollsBackTheDisambiguatedContext()
    {
        using var chosen = new SqliteConnection("DataSource=:memory:");
        chosen.Open();
        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();

        using var provider = BuildDisambiguatedProvider(chosen, other);
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Probe(Id INTEGER)");

        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(
            provider, provider.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>());

        var handling = () => behavior.HandleAsync(
            new TransactionalTestCommand("tx"),
            async () =>
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO Probe(Id) VALUES (1)");
                throw new InvalidOperationException("simulated failure");
            });

        await handling.Should().ThrowAsync<InvalidOperationException>().WithMessage("simulated failure");

        // The write is gone only if the transaction wrapped the base-disambiguated context.
        Rows(chosen).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenTwoContextsAndBaseDisambiguates_CommitsTheDisambiguatedContext()
    {
        using var chosen = new SqliteConnection("DataSource=:memory:");
        chosen.Open();
        using var other = new SqliteConnection("DataSource=:memory:");
        other.Open();

        using var provider = BuildDisambiguatedProvider(chosen, other);
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
        Rows(chosen).Should().Be(2);
    }

    private static ServiceProvider BuildDisambiguatedProvider(SqliteConnection chosen, SqliteConnection other)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ICommandHandler<TransactionalTestCommand, string>, TransactionalTestCommandHandler>();

        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(chosen));
        services.AddDbContext<SecondTransactionTestDbContext>(options => options.UseSqlite(other));
        // The explicit base picks the context the transaction wraps out of the two registered.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        return services.BuildServiceProvider();
    }

    private static long Rows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Probe";

        return (long)command.ExecuteScalar()!;
    }
}
