using Bsft.Messaging.EFCore.Postgres.DependencyInjection;
using Bsft.Messaging.EFCore.Postgres.Tests.Fakes;
using Bsft.Messaging.EFCore.Postgres.Tests.Support;
using Bsft.Messaging.Outbox;
using Bsft.Outbox.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests.DependencyInjection;

public class PostgresServiceCollectionExtensionsTests
{
    private static ServiceCollection Host()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<TestOutboxDbContext>(options => options.UseSqlite("DataSource=:memory:"));

        // The dispatcher half, which AddBsftMessagingEFCore supplies in a real host. The worker drains
        // through it and is registered as a hosted service, so it has to be resolvable here too.
        services.AddSingleton<IOutboxProcessor>(new ScriptedOutboxProcessor());

        return services;
    }

    private static ServiceProvider Build(Action<BsftMessagingPostgresOptions>? configure = null)
    {
        var services = Host();
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>(configure);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_RegistersTheNotifyingHalf()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IOutboxNotifier>().Should().BeOfType<PostgresOutboxNotifier>();
        scope.ServiceProvider.GetService<OutboxNotifyInterceptor>().Should().NotBeNull();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_RegistersTheListeningHalf()
    {
        using var provider = Build();

        provider.GetService<IOutboxNotificationChannel>()
            .Should().BeOfType<PostgresOutboxNotificationChannel<TestOutboxDbContext>>();

        provider.GetServices<IHostedService>().Should().ContainSingle()
            .Which.Should().BeOfType<OutboxNotificationWorker>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_ListensThroughTheSharedSession()
    {
        using var provider = Build();

        provider.GetRequiredService<IPostgresListenSession>().Should().BeOfType<PostgresListenSession>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_KeepsASessionTheHostRegistered()
    {
        var session = new RecordingListenSession();
        var services = Host();
        services.AddSingleton<IPostgresListenSession>(session);
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPostgresListenSession>().Should().BeSameAs(session);
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_TheActionConfiguresTheOptions()
    {
        using var provider = Build(options => options.NotificationChannel = "orders_outbox");

        provider.GetRequiredService<IOptions<BsftMessagingPostgresOptions>>()
            .Value.NotificationChannel.Should().Be("orders_outbox");
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_BindsTheSectionAndLetsTheActionOverrideIt()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftMessagingPostgresOptions.SectionName}:NotificationChannel"] = "from_configuration",
                [$"{BsftMessagingPostgresOptions.SectionName}:ReconnectDelay"] = "00:00:30",
            })
            .Build();

        var services = Host();
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>(
            configuration, options => options.NotificationChannel = "from_code");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BsftMessagingPostgresOptions>>().Value;

        options.NotificationChannel.Should().Be("from_code");
        options.ReconnectDelay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_AnInvalidChannel_FailsTheStart()
    {
        using var provider = Build(options => options.NotificationChannel = "Not A Channel");

        var read = () => provider.GetRequiredService<IOptions<BsftMessagingPostgresOptions>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_IsIdempotent()
    {
        var services = Host();
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>();
        services.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IOutboxNotificationChannel>().Should().ContainSingle();
        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_RejectsMissingArguments()
    {
        var configuration = new ConfigurationBuilder().Build();

        var withoutServices = () =>
            PostgresServiceCollectionExtensions.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>(null!);

        var withoutServicesAndConfiguration = () =>
            PostgresServiceCollectionExtensions.AddBsftMessagingEFCorePostgres<TestOutboxDbContext>(
                null!, configuration);

        var withoutConfiguration = () =>
            new ServiceCollection().AddBsftMessagingEFCorePostgres<TestOutboxDbContext>((IConfiguration)null!);

        withoutServices.Should().Throw<ArgumentNullException>();
        withoutServicesAndConfiguration.Should().Throw<ArgumentNullException>();
        withoutConfiguration.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_WithInProcessWakeEnabled_ResolvesOneInProcessOutboxWake()
    {
        using var provider = Build(options => options.IsInProcessWake = true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IOutboxNotifier>()
            .Should().BeOfType<InProcessOutboxWake>();
        scope.ServiceProvider.GetService<IOutboxNotificationChannel>()
            .Should().BeOfType<InProcessOutboxWake>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_WithInProcessWakeDisabled_ResolvesPostgresTypes()
    {
        using var provider = Build(options => options.IsInProcessWake = false);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IOutboxNotifier>()
            .Should().BeOfType<PostgresOutboxNotifier>();
        scope.ServiceProvider.GetService<IOutboxNotificationChannel>()
            .Should().BeOfType<PostgresOutboxNotificationChannel<TestOutboxDbContext>>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_DefaultIsInProcessWakeDisabled_ResolvesPostgresTypes()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IOutboxNotifier>()
            .Should().BeOfType<PostgresOutboxNotifier>();
        scope.ServiceProvider.GetService<IOutboxNotificationChannel>()
            .Should().BeOfType<PostgresOutboxNotificationChannel<TestOutboxDbContext>>();
    }

    [Fact]
    public void AddBsftMessagingEFCorePostgres_WithInProcessWakeEnabled_UsesSingletonInstance()
    {
        using var provider = Build(options => options.IsInProcessWake = true);
        using var scope = provider.CreateScope();

        var notifier = scope.ServiceProvider.GetService<IOutboxNotifier>();
        var channel = scope.ServiceProvider.GetService<IOutboxNotificationChannel>();

        notifier.Should().BeSameAs(channel);
    }
}
