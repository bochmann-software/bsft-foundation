using Bsft.Scheduling.DependencyInjection;
using Bsft.Scheduling.EFCore.DependencyInjection;
using Bsft.Scheduling.EFCore.Tests.Support;
using Bsft.Scheduling.StoredSchedules;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests.DependencyInjection;

/// <summary>
/// Wiring the scheduler onto an application's own DbContext.
/// </summary>
public class SchedulingEFCoreServiceCollectionExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SchedulingEFCoreServiceCollectionExtensionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    [Fact]
    public void TheEntryPointRejectsANullServiceCollection()
    {
        var act = () => SchedulingEFCoreServiceCollectionExtensions.AddBsftSchedulingEFCore<TestJobDbContext>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void TheEntryPointReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddBsftSchedulingEFCore<TestJobDbContext>().Should().BeSameAs(services);
    }

    [Fact]
    public void TheContextInterfaceIsMappedOntoTheApplicationContextAsScoped()
    {
        var services = new ServiceCollection();

        services.AddBsftSchedulingEFCore<TestJobDbContext>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IJobDbContext));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void TheEfCoreStoreReplacesTheInMemoryDefaultWhenTheCoreWasRegisteredFirst()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftScheduling();
        services.AddBsftSchedulingEFCore<TestJobDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<EFCoreJobStore>();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IJobDbContext>().Should().BeOfType<TestJobDbContext>();
    }

    [Fact]
    public void TheEfCoreStoreSurvivesTheCoreRegistrationThatFollowsIt()
    {
        // AddBsftScheduling registers its in-memory default with TryAdd, so the order of the two calls
        // does not decide which store the container resolves.
        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftSchedulingEFCore<TestJobDbContext>();
        services.AddBsftScheduling();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<EFCoreJobStore>();
        provider.GetRequiredService<IJobStore>().Should().NotBeOfType<InMemoryJobStore>();
    }

    [Fact]
    public async Task TheConfiguredSchedulerActuallyPersistsARunThroughThatContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftScheduling();
        services.AddBsftSchedulingEFCore<TestJobDbContext>();

        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TestJobDbContext>().Database.EnsureCreatedAsync();

        var store = provider.GetRequiredService<IJobStore>();
        var id = await store.EnqueueAsync(new JobRun { JobName = "report", DueAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero) });

        (await store.FindAsync(id)).Should().NotBeNull();
    }

    // ---- AddBsftStoredSchedulesEFCore ----------------------------------------------------------

    [Fact]
    public void TheStoredScheduleEntryPointRejectsANullServiceCollection()
    {
        var act = () => SchedulingEFCoreServiceCollectionExtensions.AddBsftStoredSchedulesEFCore<TestStoredScheduleDbContext>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void TheStoredScheduleEntryPointReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddBsftStoredSchedulesEFCore<TestStoredScheduleDbContext>().Should().BeSameAs(services);
    }

    [Fact]
    public void TheStoredScheduleContextInterfaceIsMappedOntoTheApplicationContextAsScoped()
    {
        var services = new ServiceCollection();

        services.AddBsftStoredSchedulesEFCore<TestStoredScheduleDbContext>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IStoredScheduleDbContext));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void TheStoredScheduleEfCoreStoreReplacesTheInMemoryDefaultWhenTheCoreWasRegisteredFirst()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestStoredScheduleDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftScheduling();
        services.AddBsftStoredSchedulesEFCore<TestStoredScheduleDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStoredScheduleStore>().Should().BeOfType<EFCoreStoredScheduleStore>();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IStoredScheduleDbContext>().Should().BeOfType<TestStoredScheduleDbContext>();
    }

    [Fact]
    public void TheStoredScheduleEfCoreStoreSurvivesTheCoreRegistrationThatFollowsIt()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestStoredScheduleDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftStoredSchedulesEFCore<TestStoredScheduleDbContext>();
        services.AddBsftScheduling();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStoredScheduleStore>().Should().BeOfType<EFCoreStoredScheduleStore>();
        provider.GetRequiredService<IStoredScheduleStore>().Should().NotBeOfType<InMemoryStoredScheduleStore>();
    }

    [Fact]
    public async Task TheConfiguredStoreActuallyPersistsAScheduleThroughThatContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestStoredScheduleDbContext>(opt => opt.UseSqlite(_connection));
        services.AddBsftScheduling();
        services.AddBsftStoredSchedulesEFCore<TestStoredScheduleDbContext>();

        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TestStoredScheduleDbContext>().Database.EnsureCreatedAsync();

        var store = provider.GetRequiredService<IStoredScheduleStore>();
        var id = await store.CreateAsync(new StoredSchedule
        {
            JobKey = "report",
            Schedule = JobSchedule.Every(TimeSpan.FromMinutes(5)),
            Enabled = true,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
        });

        (await store.FindAsync(id)).Should().NotBeNull();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
