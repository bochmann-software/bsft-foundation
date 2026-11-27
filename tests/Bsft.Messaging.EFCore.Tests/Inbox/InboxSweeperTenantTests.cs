using Bsft.Core.Tenancy;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.EFCore.Tests.Fakes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Tests.Shared;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

public sealed class InboxSweeperTenantTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly SqliteConnection _connection;

    public InboxSweeperTenantTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<TimeProvider>(_clock);
        services.AddOptions<BsftMessagingOptions>()
            .Configure(o => o.Consumer.InboxRetention = TimeSpan.FromHours(1));
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreated();
        return provider;
    }

    private InboxSweeper CreateSweeper(IServiceProvider provider, BsftMessagingOptions? options = null)
    {
        var opts = options ?? new BsftMessagingOptions { Consumer = { InboxRetention = TimeSpan.FromHours(1) } };

        return new InboxSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(opts),
            _clock,
            NullLogger<InboxSweeper>.Instance);
    }

    private InboxSweeper CreateSweeperWithTenants(
        IServiceProvider provider,
        IBackgroundTenantSource tenants,
        BsftMessagingOptions? options = null)
    {
        var opts = options ?? new BsftMessagingOptions { Consumer = { InboxRetention = TimeSpan.FromHours(1) } };

        return new InboxSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(opts),
            _clock,
            NullLogger<InboxSweeper>.Instance,
            tenants);
    }

    private static BsftMessagingOptions Retained() => new() { Consumer = { InboxRetention = TimeSpan.FromHours(1) } };

    [Fact]
    public async Task RunLoopAsync_WithATenantSource_SweepsOncePerTenant()
    {
        var scopes = new FakeServiceScopeFactory();
        var sweeper = new InboxSweeper(scopes, Options.Create(Retained()), _clock, NullLogger<InboxSweeper>.Instance, new FakeBackgroundTenantSource(["a", "b"]));

        var openedFor = await RunFirstPassAsync(sweeper.RunLoopAsync, scopes);

        openedFor.Take(2).Should().Equal("a", "b");
    }

    [Fact]
    public async Task RunLoopAsync_WithoutATenantSource_SweepsWithoutATenant()
    {
        var scopes = new FakeServiceScopeFactory();
        var sweeper = new InboxSweeper(scopes, Options.Create(Retained()), _clock, NullLogger<InboxSweeper>.Instance);

        var openedFor = await RunFirstPassAsync(sweeper.RunLoopAsync, scopes);

        openedFor[0].Should().BeNull();
    }

    private async Task<IReadOnlyList<string?>> RunFirstPassAsync(
        Func<CancellationToken, Task> run, FakeServiceScopeFactory scopes)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = run(cts.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(_clock.WaitForAnyTimerAsync(), loop);

        await cts.CancelAsync();
        await loop;

        return scopes.OpenedFor;
    }

    [Fact]
    public void Constructor_WithNullTenants_Throws()
    {
        var provider = BuildProvider();

        var act = () => CreateSweeperWithTenants(provider, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenants");
    }
}
