using Bsft.Messaging.DependencyInjection;
using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Outbox;
using Bsft.Messaging.EFCore.Tests.Support;
using Bsft.Messaging.Outbox;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
