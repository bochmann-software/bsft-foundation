using Bsft.Core.Persistence;
using Bsft.Data.Postgres.DependencyInjection;
using Bsft.Data.Postgres.Tests.Support;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Bsft.Data.Postgres.Tests.DependencyInjection;

/// <summary>
/// What the PostgreSQL shorthand registers, checked without a server: the shared transaction with the
/// PostgreSQL classification, and module contexts configured onto the scope's one Npgsql connection.
/// </summary>
public sealed class PostgresSharedTransactionRegistrationTests
{
    private const string ConnectionString = "Host=unused;Database=unused";

    [Fact]
    public async Task AddBsftDataPostgresTransaction_RegistersTheSharedTransactionWithThePostgresClassification()
    {
        var services = new ServiceCollection();
        services.AddBsftDataPostgresTransaction(ConnectionString);
        services.AddBsftDataPostgresTransaction(ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        provider.GetServices<ITransactionConflictClassifier>().Should().ContainSingle();
        scope.ServiceProvider.GetRequiredService<IAmbientTransaction>()
            .IsTransactionConflict(new PostgresException("refused", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected))
            .Should().BeTrue();
    }

    [Fact]
    public async Task UseBsftSharedNpgsql_GivesEveryContextOfTheScopeTheSameNpgsqlConnection()
    {
        var services = new ServiceCollection();
        services.AddBsftDataPostgresTransaction(_ => new NpgsqlConnection(ConnectionString));
        services.AddDbContext<CatalogContext>((sp, options) => options.UseBsftSharedNpgsql(sp));
        services.AddDbContext<InventoryContext>((sp, options) => options
            .UseBsftSharedNpgsql(sp, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryContext>();

        catalog.Database.IsNpgsql().Should().BeTrue();
        catalog.Database.GetDbConnection().Should().BeOfType<NpgsqlConnection>()
            .Which.Should().BeSameAs(inventory.Database.GetDbConnection());
    }

    [Fact]
    public void AddBsftDataPostgresTransaction_WithAnUnusableArgument_Throws()
    {
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataPostgresTransaction(" "))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataPostgresTransaction((string)null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ServiceCollection()
                .AddBsftDataPostgresTransaction((Func<IServiceProvider, NpgsqlConnection>)null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ((IServiceCollection)null!).AddBsftDataPostgresTransaction(ConnectionString))
            .Should().Throw<ArgumentNullException>();
    }
}
