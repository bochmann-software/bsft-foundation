using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// The resolver caches the resolved target <c>DbContext</c> <em>type</em> on itself — a container
/// singleton — keyed by the <c>DbContextOptions</c> service type, so the <c>ContextType</c> reflection
/// runs once per application. A plain <c>AddDbContext&lt;T&gt;()</c> registers <c>DbContextOptions</c> as
/// scoped, so each DI scope hands out its own options instance; keying on that instance would rerun the
/// reflection every scope, which these tests guard against.
/// </summary>
public class TransactionDbContextResolverTests
{
    [Fact]
    public void Resolve_AcrossTwoDifferentScopes_RunsTheContextTypeReflectionOnlyOnce()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        // A plain AddDbContext<T>(): DbContextOptions is scoped, a fresh instance per scope.
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();

        var resolver = new TransactionDbContextResolver();

        using var firstScope = provider.CreateScope();
        var firstContext = resolver.Resolve(firstScope.ServiceProvider);

        using var secondScope = provider.CreateScope();
        var secondContext = resolver.Resolve(secondScope.ServiceProvider);

        // Each scope resolves its own concrete instance, proving the instance is still scoped...
        firstContext.Should().BeOfType<TransactionTestDbContext>();
        secondContext.Should().BeOfType<TransactionTestDbContext>();
        secondContext.Should().NotBeSameAs(firstContext);

        // ...but the reflection that reads the target type ran once and was served from the singleton
        // cache the second time, even though the two options instances differ.
        resolver.TargetTypeResolutions.Should().Be(1);
    }

    [Fact]
    public void Resolve_WhenNoDbContextRegistered_ReturnsNullAndCachesNothing()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        var resolver = new TransactionDbContextResolver();

        // No DbContextOptions registered, so the options lookup is null and the base-DbContext lookup
        // is null too: no context resolves and the reflection never runs.
        resolver.Resolve(provider).Should().BeNull();
        resolver.TargetTypeResolutions.Should().Be(0);
    }

    [Fact]
    public void Resolve_WhenTheOptionsNameNoContextAndNoBaseIsRegistered_ReturnsNull()
    {
        // Options are registered, but they name no context and no base DbContext is there to fall
        // back on: there is nothing to open the transaction on, which is a null, not a lookup of null.
        var services = new ServiceCollection();
        services.AddSingleton<DbContextOptions>(new ContextlessOptions());
        using var provider = services.BuildServiceProvider();

        var resolver = new TransactionDbContextResolver();

        resolver.Resolve(provider).Should().BeNull();
        resolver.TargetTypeResolutions.Should().Be(1);
    }

    [Fact]
    public void ListAmbiguousContexts_WhenServicesNull_Throws()
    {
        var resolver = new TransactionDbContextResolver();

        var act = () => resolver.ListAmbiguousContexts(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
