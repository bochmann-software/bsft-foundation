using Bsft.Core.Persistence;
using Bsft.Data.DependencyInjection;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests;

/// <summary>
/// What <c>AddBsftDataUnitOfWork</c> puts in the container: the two contracts a package that stages
/// writes is typed against, and the one instance behind both.
/// </summary>
public sealed class UnitOfWorkRegistrationTests
{
    [Fact]
    public void WithNullServicesThrows()
    {
        var act = () => DataServiceCollectionExtensions.AddBsftDataUnitOfWork<TestDbContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegistersBothContractsOverTheHostsContext()
    {
        using var provider = Configured().BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .Should().BeOfType<DbContextUnitOfWork<TestDbContext>>();
        scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>()
            .Should().BeOfType<DbContextUnitOfWork<TestDbContext>>();
    }

    /// <summary>
    /// One instance per scope behind both contracts, so a commit and a unit run on the same context
    /// rather than on two views of it.
    /// </summary>
    [Fact]
    public void ServesOneInstanceToBothContracts()
    {
        using var provider = Configured().BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>());
    }

    [Fact]
    public void StepsAsideForAHostsOwnUnitOfWork()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUnitOfWork, HostUnitOfWork>();
        services.AddDbContext<TestDbContext>(options => options.UseInMemoryDatabase("registration"));
        services.AddBsftDataUnitOfWork<TestDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<HostUnitOfWork>();
    }

    private static IServiceCollection Configured()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseInMemoryDatabase("registration"));
        services.AddBsftDataUnitOfWork<TestDbContext>();

        return services;
    }

    /// <summary>A unit of work of the host's own, spanning something this framework never sees.</summary>
    private sealed class HostUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
