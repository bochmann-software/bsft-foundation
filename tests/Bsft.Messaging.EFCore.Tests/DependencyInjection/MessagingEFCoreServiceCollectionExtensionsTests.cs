using Bsft.Core.Tenancy;
using Bsft.Outbox;
using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Outbox;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.DependencyInjection;

public sealed class MessagingEFCoreServiceCollectionExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MessagingEFCoreServiceCollectionExtensionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftMessaging();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));

        return services;
    }

    [Fact]
    public async Task With_a_tenant_scope_factory_the_outbox_store_binds_a_tenant_pass_through_it()
    {
        var services = CreateServices();
        services.AddBsftMessagingEFCore<TestOutboxDbContext>();
        services.AddSingleton<AsyncOnlyTenantScopeFactory>(
            sp => new AsyncOnlyTenantScopeFactory(sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AsyncOnlyTenantScopeFactory>());

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreatedAsync();

        var store = provider.GetRequiredService<IOutboxStore<IntegrationEventMessage>>();

        using (new BackgroundTenant("a"))
            (await store.LeaseAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 10)).Should().BeEmpty();

        provider.GetRequiredService<AsyncOnlyTenantScopeFactory>().BoundFor.Should().Equal("a");
    }

    [Fact]
    public void The_full_outbox_path_resolves_from_the_container()
    {
        var services = CreateServices();

        services.AddBsftMessagingEFCore<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOutboxStore>().Should().BeOfType<EFCoreOutboxStore>();
        provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Should().ContainSingle();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxInterceptor>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IOutboxDbContext>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>());
    }

    [Fact]
    public void The_outbox_processor_is_the_instance_the_host_runs()
    {
        var services = CreateServices();

        services.AddBsftMessagingEFCore<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();

        // The point of the seam: draining by hand has to exercise the dispatcher the application runs,
        // not a second one over the same outbox.
        var hosted = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();
        provider.GetRequiredService<IOutboxProcessor>().Should().BeSameAs(hosted);
    }

    [Fact]
    public void The_dispatcher_options_are_configurable()
    {
        var services = CreateServices();

        services.AddBsftMessagingEFCore<TestOutboxDbContext>(o =>
        {
            o.BatchSize = 5;
            o.LeaseDuration = TimeSpan.FromSeconds(42);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingOptions>>().Value.Outbox;
        options.BatchSize.Should().Be(5);
        options.LeaseDuration.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void The_staging_side_can_be_registered_without_the_dispatcher()
    {
        var services = CreateServices();

        services.AddBsftOutbox<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().Should().BeEmpty();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxInterceptor>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddBsftMessagingEFCore_WithABackgroundTenantSource_DrainsTheOutboxOncePerTenant()
    {
        var drainer = new TenantRecordingOutboxDrainer();
        var services = CreateServices();
        services.AddBsftMessagingEFCore<TestOutboxDbContext>();
        services.AddSingleton<IBackgroundTenantSource>(new FakeBackgroundTenantSource(["a", "b"]));
        services.Replace(ServiceDescriptor.Singleton<IOutboxDrainer<IntegrationEventMessage>>(drainer));

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

        await dispatcher.ProcessOnceAsync();

        drainer.DrainedFor.Should().Equal("a", "b");
    }

    [Fact]
    public async Task AddBsftMessagingEFCore_WithoutABackgroundTenantSource_DrainsTheOutboxWithoutATenant()
    {
        var drainer = new TenantRecordingOutboxDrainer();
        var services = CreateServices();
        services.AddBsftMessagingEFCore<TestOutboxDbContext>();
        services.Replace(ServiceDescriptor.Singleton<IOutboxDrainer<IntegrationEventMessage>>(drainer));

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

        await dispatcher.ProcessOnceAsync();

        drainer.DrainedFor.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task AddBsftInboxEFCore_WithABackgroundTenantSource_InboxSweeperSweepsOncePerTenant()
    {
        var recorder = new TenantRecorder<IInboxDbContext>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftMessaging(o => o.Consumer.InboxRetention = TimeSpan.FromHours(1));
        services.AddBsftInboxEFCore<TestOutboxDbContext>();
        services.AddSingleton<IBackgroundTenantSource>(new FakeBackgroundTenantSource(["a", "b"]));
        // The sweeper resolves its context from the scope of each pass; recording here shows whom it swept for.
        services.Replace(ServiceDescriptor.Scoped<IInboxDbContext>(_ => recorder.Record()));

        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        services.AddSingleton<TimeProvider>(clock);

        using var provider = services.BuildServiceProvider();
        var sweeper = provider.GetServices<IHostedService>().OfType<InboxSweeper>().Single();

        var openedFor = await RunFirstPassAsync(sweeper.RunLoopAsync, recorder, clock);

        openedFor.Take(2).Should().Equal("a", "b");
    }

    [Fact]
    public async Task AddBsftInboxEFCore_WithoutABackgroundTenantSource_InboxSweeperSweepsWithoutATenant()
    {
        var recorder = new TenantRecorder<IInboxDbContext>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftMessaging(o => o.Consumer.InboxRetention = TimeSpan.FromHours(1));
        services.AddBsftInboxEFCore<TestOutboxDbContext>();
        // The sweeper resolves its context from the scope of each pass; recording here shows whom it swept for.
        services.Replace(ServiceDescriptor.Scoped<IInboxDbContext>(_ => recorder.Record()));

        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        services.AddSingleton<TimeProvider>(clock);

        using var provider = services.BuildServiceProvider();
        var sweeper = provider.GetServices<IHostedService>().OfType<InboxSweeper>().Single();

        var openedFor = await RunFirstPassAsync(sweeper.RunLoopAsync, recorder, clock);

        openedFor.Should().NotBeEmpty().And.OnlyContain(tenant => tenant == null);
    }

    private static async Task<IReadOnlyList<string?>> RunFirstPassAsync(
        Func<CancellationToken, Task> run, TenantRecorder<IInboxDbContext> recorder, ManualTimeProvider clock)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = run(cts.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(clock.WaitForAnyTimerAsync(), loop).ConfigureAwait(false);

        await cts.CancelAsync();
        await loop.ConfigureAwait(false);

        return recorder.ResolvedFor;
    }

    [Fact]
    public void A_null_service_collection_is_rejected()
    {
        var addOutbox = () => MessagingEFCoreServiceCollectionExtensions.AddBsftOutbox<TestOutboxDbContext>(null!);
        var addFullPath = () => MessagingEFCoreServiceCollectionExtensions.AddBsftMessagingEFCore<TestOutboxDbContext>(null!);
        var addInbox = () => MessagingEFCoreServiceCollectionExtensions.AddBsftInboxEFCore<TestOutboxDbContext>(null!);

        addOutbox.Should().Throw<ArgumentNullException>().WithParameterName("services");
        addFullPath.Should().Throw<ArgumentNullException>().WithParameterName("services");
        addInbox.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
