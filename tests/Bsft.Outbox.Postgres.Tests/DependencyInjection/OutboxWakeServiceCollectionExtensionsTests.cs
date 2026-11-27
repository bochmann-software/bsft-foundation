using Bsft.Outbox.Postgres.DependencyInjection;
using Bsft.Outbox.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Bsft.Outbox.Postgres.Tests.DependencyInjection;

/// <summary>Tests what registering the PostgreSQL wake-up of an outbox gives a host.</summary>
public sealed class OutboxWakeServiceCollectionExtensionsTests
{
    private static void Valid(BsftOutboxPostgresOptions options)
    {
        options.NotificationChannel = "orders_outbox";
        options.ConnectionString = "Host=localhost";
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_RegistersTheSignalUnderTheOutboxNameAndOnTheHost()
    {
        using var provider = Provider(services => services.AddBsftOutboxPostgresWake("orders", Valid));

        var signal = provider.GetRequiredKeyedService<IOutboxWakeSignal>("orders");

        signal.Should().BeOfType<PostgresOutboxWakeSignal>();
        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<PostgresOutboxWakeWorker>();
        provider.GetService<IOutboxWakeSignal>().Should().BeNull();
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_WhenCalledTwiceForOneOutbox_RegistersOneSignal()
    {
        using var provider = Provider(services =>
        {
            services.AddBsftOutboxPostgresWake("orders", Valid);
            services.AddBsftOutboxPostgresWake("orders", Valid);
        });

        provider.GetServices<IHostedService>().Should().ContainSingle();
        provider.GetServices<IPostgresListenSession>().Should().ContainSingle();
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_ForTwoOutboxes_RegistersASignalEach()
    {
        using var provider = Provider(services =>
        {
            services.AddBsftOutboxPostgresWake("orders", Valid);
            services.AddBsftOutboxPostgresWake("invoices", options =>
            {
                Valid(options);
                options.NotificationChannel = "invoices_outbox";
            });
        });

        provider.GetRequiredKeyedService<IOutboxWakeSignal>("orders")
            .Should().NotBeSameAs(provider.GetRequiredKeyedService<IOutboxWakeSignal>("invoices"));
        provider.GetServices<IHostedService>().Should().HaveCount(2);
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_WithAHostSession_KeepsIt()
    {
        var session = new ScriptedListenSession((_, _, _) => Task.CompletedTask);

        using var provider = Provider(services =>
        {
            services.AddSingleton<IPostgresListenSession>(session);
            services.AddBsftOutboxPostgresWake("orders", Valid);
        });

        provider.GetRequiredService<IPostgresListenSession>().Should().BeSameAs(session);
    }

    [Fact]
    public async Task TheSignal_WhenTheHostStarts_ListensOnTheChannelOfItsOutbox()
    {
        var session = new ScriptedListenSession((_, _, token) => Task.Delay(Timeout.Infinite, token));
        using var provider = Provider(services =>
        {
            services.AddSingleton<IPostgresListenSession>(session);
            services.AddBsftOutboxPostgresWake("orders", Valid);
        });
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await session.FirstSessionStarted;
        await hosted.StopAsync(CancellationToken.None);

        session.Last.Should().Be(("Host=localhost", "orders_outbox"));
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_WithConfiguration_BindsTheSectionOfThatOutboxAndLetsTheActionWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BsftOutboxPostgresOptions.SectionName}:orders:NotificationChannel"] = "bound_channel",
                [$"{BsftOutboxPostgresOptions.SectionName}:orders:ConnectionString"] = "Host=bound",
                [$"{BsftOutboxPostgresOptions.SectionName}:orders:ReconnectDelay"] = "00:00:09",
            })
            .Build();

        using var provider = Provider(services => services.AddBsftOutboxPostgresWake(
            "orders", configuration, options => options.ConnectionString = "Host=code"));

        var options = provider.GetRequiredService<IOptionsMonitor<BsftOutboxPostgresOptions>>().Get("orders");
        options.NotificationChannel.Should().Be("bound_channel");
        options.ConnectionString.Should().Be("Host=code");
        options.ReconnectDelay.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_WithoutAChannel_IsRefusedBeforeTheHostStarts()
    {
        using var provider = Provider(services => services.AddBsftOutboxPostgresWake("orders"));

        var act = () => provider.GetRequiredService<IOptionsMonitor<BsftOutboxPostgresOptions>>().Get("orders");

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(BsftOutboxPostgresOptions.NotificationChannel)}*");
    }

    [Fact]
    public void AddBsftOutboxPostgresWake_WithAMissingArgument_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        var cases = new (string Parameter, Action Call)[]
        {
            ("services", () => OutboxWakeServiceCollectionExtensions.AddBsftOutboxPostgresWake(null!, "orders")),
            ("name", () => new ServiceCollection().AddBsftOutboxPostgresWake(null!)),
            ("services", () => OutboxWakeServiceCollectionExtensions.AddBsftOutboxPostgresWake(null!, "orders", configuration)),
            ("name", () => new ServiceCollection().AddBsftOutboxPostgresWake(null!, configuration)),
            ("configuration", () => new ServiceCollection().AddBsftOutboxPostgresWake("orders", (IConfiguration)null!)),
        };

        foreach (var (parameter, call) in cases)
            call.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    /// <summary>The whole arrangement on a real server: a write announced on the channel ends the worker's idle wait.</summary>
    [PostgresFact]
    public async Task TheSignal_WhenARowIsAnnouncedOnTheServer_CompletesAWait()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgresTestServer.ConnectionStringVariable)!;
        using var provider = Provider(services => services.AddBsftOutboxPostgresWake("orders", options =>
        {
            options.NotificationChannel = "bsft_outbox_it_signal";
            options.ConnectionString = connectionString;
        }));
        var hosted = provider.GetServices<IHostedService>().Single();
        var signal = provider.GetRequiredKeyedService<IOutboxWakeSignal>("orders");

        await hosted.StartAsync(CancellationToken.None);
        await signal.WaitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var wait = signal.WaitAsync();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var notify = new NpgsqlCommand("SELECT pg_notify('bsft_outbox_it_signal', '')", connection);
            await notify.ExecuteNonQueryAsync();
        }

        await wait.WaitAsync(TimeSpan.FromSeconds(30));
        await hosted.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        configure(services);

        return services.BuildServiceProvider();
    }
}
