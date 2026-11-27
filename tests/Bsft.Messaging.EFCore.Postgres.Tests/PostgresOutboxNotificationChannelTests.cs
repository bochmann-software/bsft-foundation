using Bsft.Messaging.EFCore.Postgres.Tests.Fakes;
using Bsft.Messaging.EFCore.Postgres.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public class PostgresOutboxNotificationChannelTests
{
    private readonly RecordingListenSession _session = new();

    private PostgresOutboxNotificationChannel<TestOutboxDbContext> Channel(
        BsftMessagingPostgresOptions options, Action<DbContextOptionsBuilder>? database = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestOutboxDbContext>(builder =>
        {
            if (database is null)
                builder.UseSqlite("DataSource=application.db");
            else
                database(builder);
        });

        return new PostgresOutboxNotificationChannel<TestOutboxDbContext>(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            _session);
    }

    [Fact]
    public async Task ListenAsync_WithoutAConnectionStringOfItsOwn_ListensOnTheConnectionOfTheContext()
    {
        var channel = Channel(new BsftMessagingPostgresOptions { NotificationChannel = "orders_outbox" });

        await channel.ListenAsync(_ => Task.CompletedTask);

        _session.ConnectionString.Should().Be("DataSource=application.db");
        _session.Channel.Should().Be("orders_outbox");
    }

    [Fact]
    public async Task ListenAsync_WithAConnectionStringOfItsOwn_ListensOnThat()
    {
        var channel = Channel(new BsftMessagingPostgresOptions { ConnectionString = "Host=listener" });

        await channel.ListenAsync(_ => Task.CompletedTask);

        _session.ConnectionString.Should().Be("Host=listener");
        _session.Channel.Should().Be(PostgresDefaults.NotificationChannel);
    }

    [Fact]
    public async Task ListenAsync_HandsTheCallbackToTheSession()
    {
        var calls = 0;
        var channel = Channel(new BsftMessagingPostgresOptions { ConnectionString = "Host=listener" });

        await channel.ListenAsync(_ =>
        {
            calls++;

            return Task.CompletedTask;
        });

        calls.Should().Be(1);
        _session.Callbacks.Should().Be(1);
    }

    [Fact]
    public async Task ListenAsync_WhenNeitherTheOptionsNorTheContextCarryAConnectionString_Throws()
    {
        using var connection = new SqliteConnection();
        var channel = Channel(new BsftMessagingPostgresOptions(), builder => builder.UseSqlite(connection));

        var act = () => channel.ListenAsync(_ => Task.CompletedTask);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TestOutboxDbContext*");
    }

    [Fact]
    public async Task ListenAsync_WithoutACallback_Throws()
    {
        var channel = Channel(new BsftMessagingPostgresOptions());

        var act = () => channel.ListenAsync(null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("onNotified");
    }

    [Fact]
    public void Constructor_WhenAnArgumentIsNull_Throws()
    {
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new BsftMessagingPostgresOptions());

        var cases = new (string Parameter, Action Build)[]
        {
            ("scopeFactory", () => new PostgresOutboxNotificationChannel<TestOutboxDbContext>(null!, options, _session)),
            ("options", () => new PostgresOutboxNotificationChannel<TestOutboxDbContext>(scopes, null!, _session)),
            ("session", () => new PostgresOutboxNotificationChannel<TestOutboxDbContext>(scopes, options, null!)),
        };

        foreach (var (parameter, build) in cases)
            build.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }
}
