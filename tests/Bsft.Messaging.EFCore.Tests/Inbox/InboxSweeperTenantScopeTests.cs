using Bsft.Tests.Shared;
using Bsft.Core.Tenancy;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

/// <summary>
/// The scope of a tenant's sweep is bound through <see cref="ITenantScopeFactory.CreateScopeAsync"/> and disposed
/// asynchronously.
/// </summary>
public sealed class InboxSweeperTenantScopeTests : IDisposable
{
    private const string Consumer = "orders";
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    private readonly SqliteConnection _connection;
    private readonly ManualTimeProvider _time = new(Now);
    private readonly List<string?> _boundFor = [];
    private readonly ServiceProvider _provider;

    public InboxSweeperTenantScopeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<AsyncOnlyTenantScopeFactory.BoundTenant>();
        services.AddScoped<AsyncOnlyDisposable>();
        services.AddScoped<IInboxDbContext>(sp =>
        {
            sp.GetRequiredService<AsyncOnlyDisposable>();
            lock (_boundFor)
                _boundFor.Add(sp.GetRequiredService<AsyncOnlyTenantScopeFactory.BoundTenant>().TenantId);
            return sp.GetRequiredService<TestOutboxDbContext>();
        });

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RunLoopAsync_WithAnAsyncOnlyTenantScopeFactory_BindsEachTenantAsynchronouslyAndSweeps()
    {
        await RecordExpiredAsync();
        var tenantScopes = NewTenantScopes();

        await RunFirstPassAsync(CreateSweeper(tenantScopes));

        tenantScopes.BoundFor.Take(2).Should().Equal("a", "b");
        Snapshot().Take(2).Should().Equal("a", "b");
        (await CountRecordsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunLoopAsync_WithAnAsyncOnlyTenantScopeFactory_DisposesEveryScopeAsynchronously()
    {
        var tenantScopes = NewTenantScopes();

        await RunFirstPassAsync(CreateSweeper(tenantScopes));

        tenantScopes.AsyncDisposals.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task RunLoopAsync_WhenOneTenantCannotBeBound_SweepsTheOthers()
    {
        await RecordExpiredAsync();
        var tenantScopes = NewTenantScopes();
        tenantScopes.FailingTenant = "a";

        await RunFirstPassAsync(CreateSweeper(tenantScopes));

        tenantScopes.BoundFor.Take(2).Should().Equal("a", "b");
        (await CountRecordsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PurgeOnceAsync_WithoutATenantScopeFactory_SweepsInAPlainScopeDisposedAsynchronously()
    {
        await RecordExpiredAsync();
        var sweeper = new InboxSweeper(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options(),
            _time,
            NullLogger<InboxSweeper>.Instance,
            new FakeBackgroundTenantSource(["a"]));

        // The sweep resolves a service that is only IAsyncDisposable: a scope disposed synchronously throws.
        var pruned = await sweeper.PurgeOnceAsync(Retention);

        pruned.Should().Be(1);
        Snapshot().Should().Equal([null]);
    }

    [Fact]
    public void Constructor_WithNullTenantScopes_Throws()
    {
        var act = () => CreateSweeper(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantScopes");
    }

    private static Microsoft.Extensions.Options.IOptions<BsftMessagingOptions> Options() =>
        TestMessagingOptions.For(new ConsumerOptions { ConsumerName = Consumer, InboxRetention = Retention });

    private AsyncOnlyTenantScopeFactory NewTenantScopes() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>());

    private InboxSweeper CreateSweeper(ITenantScopeFactory tenantScopes) =>
        new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options(),
            _time,
            NullLogger<InboxSweeper>.Instance,
            new FakeBackgroundTenantSource(["a", "b"]),
            tenantScopes);

    private async Task RecordExpiredAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
        db.InboxMessages.Add(new InboxMessageEntity
        {
            EventId = Guid.NewGuid(),
            Consumer = Consumer,
            EventName = "orders.placed.v1",
            ReceivedAt = (Now - TimeSpan.FromHours(2)).UtcDateTime,
        });

        await db.SaveChangesAsync();
    }

    private async Task<int> CountRecordsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().InboxMessages.CountAsync();
    }

    private async Task RunFirstPassAsync(InboxSweeper sweeper)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = sweeper.RunLoopAsync(cts.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(_time.WaitForAnyTimerAsync(), loop);

        await cts.CancelAsync();
        await loop;
    }

    private List<string?> Snapshot()
    {
        lock (_boundFor)
            return [.. _boundFor];
    }
}
