using Bsft.Core;
using Bsft.Core.Persistence;
using Bsft.Data.DependencyInjection;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.DependencyInjection;

/// <summary>
/// What <c>AddBsftDataSharedTransaction</c> and <c>UseBsftSharedTransaction</c> register: one instance
/// per scope behind the three persistence seams, and a context configured onto the scope's connection.
/// </summary>
public sealed class SharedTransactionRegistrationTests : IDisposable
{
    private readonly SharedTransactionDatabase _database = new();

    [Fact]
    public async Task AddBsftDataSharedTransaction_RegistersOneScopedInstanceBehindEverySeam()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var other = provider.CreateAsyncScope();

        var ambient = scope.ServiceProvider.GetRequiredService<IAmbientTransaction>();

        scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>().Should().BeSameAs(ambient);
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeSameAs(ambient);
        other.ServiceProvider.GetRequiredService<IAmbientTransaction>().Should().NotBeSameAs(ambient);
        provider.GetServices<IStartupValidator>().Should().ContainSingle(validator =>
            validator.GetType().Name == "SharedTransactionStartupValidator");
    }

    [Fact]
    public async Task UseBsftSharedTransaction_GivesEveryContextOfTheScopeTheSameConnection()
    {
        await using var provider = _database.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var other = provider.CreateAsyncScope();

        var catalog = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockContext>();

        stock.Database.GetDbConnection().Should().BeSameAs(catalog.Database.GetDbConnection());
        other.ServiceProvider.GetRequiredService<CatalogContext>().Database.GetDbConnection()
            .Should().NotBeSameAs(catalog.Database.GetDbConnection());
    }

    [Fact]
    public void AddBsftDataSharedTransaction_WithANullArgument_Throws()
    {
        FluentActions.Invoking(() => ((IServiceCollection)null!).AddBsftDataSharedTransaction(_ => new SqliteConnection()))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ServiceCollection().AddBsftDataSharedTransaction(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseBsftSharedTransaction_WithANullArgument_Throws()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<CatalogContext>();

        FluentActions.Invoking(() => ((DbContextOptionsBuilder)null!).UseBsftSharedTransaction(provider, (_, _) => { }))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => builder.UseBsftSharedTransaction(null!, (_, _) => { }))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => builder.UseBsftSharedTransaction(provider, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseBsftSharedTransaction_WithoutTheSharedTransaction_NamesTheMissingRegistration()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CatalogContext>((sp, options) => options
            .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection)));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<CatalogContext>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddBsftDataSharedTransaction*");
    }

    public void Dispose() => _database.Dispose();

}
