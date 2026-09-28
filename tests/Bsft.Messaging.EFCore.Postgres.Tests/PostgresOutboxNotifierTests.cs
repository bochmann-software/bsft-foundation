using Bsft.Messaging.EFCore.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Messaging.EFCore.Postgres.Tests;

public sealed class PostgresOutboxNotifierTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecordingLogger<PostgresOutboxNotifier> _logger = new();

    public PostgresOutboxNotifierTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TestOutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    private PostgresOutboxNotifier CreateNotifier(string channel = "bsft_outbox") =>
        new(Options.Create(new BsftMessagingPostgresOptions { NotificationChannel = channel }), _logger);

    [Fact]
    public async Task NotifyAsync_SendsThePgNotifyCallThroughTheCallersContext()
    {
        // The statement is asserted through the database that refuses it: SQLite has no pg_notify, so
        // its complaint names the function this package sent, which proves the call went out on the
        // context's own connection rather than on one this opened for itself.
        await using var context = CreateContext();

        await CreateNotifier().NotifyAsync(context);

        _logger.Entries.Should().ContainSingle().Which.Exception!.Message.Should().Contain("pg_notify");
    }

    [Fact]
    public void Notify_SendsThePgNotifyCallSynchronously()
    {
        using var context = CreateContext();

        CreateNotifier().Notify(context);

        _logger.Entries.Should().ContainSingle().Which.Exception!.Message.Should().Contain("pg_notify");
    }

    [Fact]
    public async Task NotifyAsync_AFailedAnnouncement_IsReportedAndSwallowed()
    {
        // The rows are committed by the time this runs, so failing the caller's save over a wake-up it
        // does not need would turn an optimisation into an outage. What is lost is the head start.
        await using var context = CreateContext();

        var announce = async () => await CreateNotifier().NotifyAsync(context);

        await announce.Should().NotThrowAsync();

        _logger.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Warning);
        _logger.Messages[0].Should().Contain("bsft_outbox");
    }

    [Fact]
    public void Notify_NamesTheConfiguredChannelInWhatItReports()
    {
        using var context = CreateContext();

        CreateNotifier("orders_outbox").Notify(context);

        _logger.Messages.Should().ContainSingle().Which.Should().Contain("orders_outbox");
    }

    /// <summary>
    /// Teaches the SQLite connection a <c>pg_notify</c> of its own, so the statement this package
    /// sends can be run to completion and the channel it bound as a parameter can be read off it.
    /// It is the closest a test gets to the server without one: the SQL, the parameter and the
    /// connection are the real ones, and only the function behind them is the test's.
    /// </summary>
    private List<string> InterceptPgNotify()
    {
        var announced = new List<string>();

        _connection.CreateFunction<string, string, long>(
            "pg_notify",
            (channel, _) =>
            {
                announced.Add(channel);

                return 1;
            });

        return announced;
    }

    [Fact]
    public async Task NotifyAsync_ASuccessfulAnnouncement_BindsTheChannelAndReportsNothing()
    {
        var announced = InterceptPgNotify();

        await using var context = CreateContext();

        await CreateNotifier("orders_outbox").NotifyAsync(context);

        announced.Should().ContainSingle().Which.Should().Be("orders_outbox");
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Notify_ASuccessfulSynchronousAnnouncement_BindsTheChannelAndReportsNothing()
    {
        var announced = InterceptPgNotify();

        using var context = CreateContext();

        CreateNotifier("orders_outbox").Notify(context);

        announced.Should().ContainSingle().Which.Should().Be("orders_outbox");
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Notify_RejectsAMissingContext()
    {
        var notifier = CreateNotifier();

        var synchronously = () => notifier.Notify(null!);
        var asynchronously = async () => await notifier.NotifyAsync(null!);

        synchronously.Should().Throw<ArgumentNullException>();
        await asynchronously.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsMissingCollaborators()
    {
        var withoutOptions = () => new PostgresOutboxNotifier(null!, _logger);
        var withoutLogger = () => new PostgresOutboxNotifier(
            Options.Create(new BsftMessagingPostgresOptions()), null!);

        withoutOptions.Should().Throw<ArgumentNullException>();
        withoutLogger.Should().Throw<ArgumentNullException>();
    }
}
