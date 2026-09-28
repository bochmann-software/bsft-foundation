using Bsft.Messaging.EFCore.DependencyInjection;
using Bsft.Messaging.EFCore.Inbox;
using Bsft.Messaging.EFCore.Tests.Fakes;
using Bsft.Messaging.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Messaging.EFCore.Tests.Inbox;

public sealed class InboxSweeperTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FixedTimeProvider _time = new(Now);
    private readonly ServiceProvider _provider;

    public InboxSweeperTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        services.AddBsftInboxEFCore<TestOutboxDbContext>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private InboxSweeper CreatePruner(
        TimeSpan? retention,
        string consumerName = "orders",
        IServiceScopeFactory? scopeFactory = null,
        ILogger<InboxSweeper>? logger = null) =>
        new(scopeFactory ?? _provider.GetRequiredService<IServiceScopeFactory>(),
            TestMessagingOptions.For(new ConsumerOptions { ConsumerName = consumerName, InboxRetention = retention }),
            _time,
            logger ?? NullLogger<InboxSweeper>.Instance);

    private async Task RecordAsync(string consumer, TimeSpan age)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        db.InboxMessages.Add(new InboxMessageEntity
        {
            EventId = Guid.NewGuid(),
            Consumer = consumer,
            EventName = "orders.placed.v1",
            ReceivedAt = (Now - age).UtcDateTime,
        });

        await db.SaveChangesAsync();
    }

    private async Task<List<InboxMessageEntity>> ReadInboxAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();

        return await db.InboxMessages.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task Only_records_older_than_the_retention_are_pruned()
    {
        // The fresh one is what still refuses a redelivery, and dropping it would reopen the duplicate
        // window the retention is supposed to define rather than shorten by accident.
        await RecordAsync("orders", age: TimeSpan.FromHours(2));
        await RecordAsync("orders", age: TimeSpan.FromMinutes(5));

        var pruned = await CreatePruner(TimeSpan.FromHours(1)).PurgeOnceAsync(TimeSpan.FromHours(1));

        pruned.Should().Be(1);
        var remaining = await ReadInboxAsync();
        remaining.Should().ContainSingle().Which.ReceivedAt.Should().Be((Now - TimeSpan.FromMinutes(5)).UtcDateTime);
    }

    [Fact]
    public async Task A_record_exactly_at_the_retention_is_kept()
    {
        // The boundary belongs to the record: the retention is how long it is kept, so it expires after
        // that long and not on reaching it.
        await RecordAsync("orders", age: TimeSpan.FromHours(1));

        var pruned = await CreatePruner(TimeSpan.FromHours(1)).PurgeOnceAsync(TimeSpan.FromHours(1));

        pruned.Should().Be(0);
        (await ReadInboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Another_consumer_s_records_are_left_alone()
    {
        // The table may be shared, and a sweep here knows nothing about the retention that consumer
        // configured — or whether it configured one at all.
        await RecordAsync("orders", age: TimeSpan.FromHours(2));
        await RecordAsync("billing", age: TimeSpan.FromHours(2));

        var pruned = await CreatePruner(TimeSpan.FromHours(1)).PurgeOnceAsync(TimeSpan.FromHours(1));

        pruned.Should().Be(1);
        (await ReadInboxAsync()).Should().ContainSingle().Which.Consumer.Should().Be("billing");
    }

    [Fact]
    public async Task Without_a_retention_the_loop_leaves_at_once_and_prunes_nothing()
    {
        // Off by default, and off means no timer and no query: the loop is given a token nobody ever
        // cancels, so a pruner that did not leave would hang this test rather than pass it.
        await RecordAsync("orders", age: TimeSpan.FromDays(400));

        await CreatePruner(retention: null).RunLoopAsync(CancellationToken.None);

        (await ReadInboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task The_loop_prunes_what_has_expired_and_stops_when_it_is_asked_to()
    {
        await RecordAsync("orders", age: TimeSpan.FromHours(2));
        await RecordAsync("orders", age: TimeSpan.FromMinutes(1));

        // Stopped once the sweep has reported what it pruned, which happens only after the delete has
        // returned, rather than by a timer the sweep is hoped to beat on a busy machine.
        var swept = new SignallingLogger<InboxSweeper>(LogLevel.Information);
        using var cts = new CancellationTokenSource();

        var loop = CreatePruner(TimeSpan.FromHours(1), logger: swept).RunLoopAsync(cts.Token);

        await Task.WhenAny(swept.Reached, loop);
        swept.Reached.IsCompletedSuccessfully.Should().BeTrue();

        await cts.CancelAsync();
        await loop;

        (await ReadInboxAsync()).Should().ContainSingle()
            .Which.ReceivedAt.Should().Be((Now - TimeSpan.FromMinutes(1)).UtcDateTime);
    }

    [Fact]
    public async Task A_sweep_that_failed_does_not_take_the_service_down()
    {
        // A database that is briefly unreachable must not stop the pruning for the life of the process;
        // the rows a failed sweep left behind are simply that much older when the next one runs. The
        // failure is modelled by a scope that cannot produce the context at all, which is the shape
        // every store failure reaches the loop in.
        using var emptyProvider = new ServiceCollection().BuildServiceProvider();

        await ASweepThatWentWrongIsSurvivedAsync(emptyProvider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task Starting_it_as_a_hosted_service_sweeps_without_anything_else_running()
    {
        // The pruner is its own hosted service rather than a step of the consumer host, because the
        // consumer host lives a package below and cannot see a DbContext. Starting it on its own is
        // therefore how it actually runs.
        await RecordAsync("orders", age: TimeSpan.FromHours(2));
        var swept = new SignallingLogger<InboxSweeper>(LogLevel.Information);
        var pruner = CreatePruner(TimeSpan.FromHours(1), logger: swept);

        await pruner.StartAsync(CancellationToken.None);

        // WhenAny against the service's own task rather than a bare await, so a sweep that never happens
        // fails this test instead of hanging it.
        await Task.WhenAny(swept.Reached, pruner.ExecuteTask!);
        await pruner.StopAsync(CancellationToken.None);

        swept.Reached.IsCompletedSuccessfully.Should().BeTrue();
        (await ReadInboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_sweep_cancelled_on_a_token_of_its_own_does_not_take_the_service_down()
    {
        // The command-timeout shape. Read as a shutdown it would escape the loop unhandled and take the
        // host down over one slow statement, so it has to count as a failed sweep instead.
        var services = new ServiceCollection();
        services.AddScoped<IInboxDbContext, SelfCancelingInboxDbContext>();
        using var cancelling = services.BuildServiceProvider();

        await ASweepThatWentWrongIsSurvivedAsync(cancelling.GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>
    /// Runs the loop until it has reported a sweep it could not complete, then stops it, and answers for
    /// the loop having survived that sweep rather than surfacing it.
    /// </summary>
    /// <remarks>
    /// Waiting for the report rather than for a duration is what makes this prove anything: a loop that
    /// never got as far as its first sweep would also not throw, so a timed version passes for the wrong
    /// reason on a busy machine. Here the sweep provably ran, provably went wrong, and the loop provably
    /// stayed up.
    /// </remarks>
    private async Task ASweepThatWentWrongIsSurvivedAsync(IServiceScopeFactory scopeFactory)
    {
        var reported = new SignallingLogger<InboxSweeper>(LogLevel.Error);
        using var cts = new CancellationTokenSource();

        var loop = CreatePruner(TimeSpan.FromHours(1), scopeFactory: scopeFactory, logger: reported)
            .RunLoopAsync(cts.Token);

        await Task.WhenAny(reported.Reached, loop);
        await cts.CancelAsync();

        var act = async () => await loop;

        await act.Should().NotThrowAsync();
        reported.Reached.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_retention_that_would_expire_every_record_at_once_is_rejected(int retentionSeconds)
    {
        var act = () => CreatePruner(TimeSpan.FromSeconds(retentionSeconds));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var options = TestMessagingOptions.For(new ConsumerOptions { ConsumerName = "orders" });
        var logger = NullLogger<InboxSweeper>.Instance;

        var withoutScopes = () => new InboxSweeper(null!, options, _time, logger);
        var withoutOptions = () => new InboxSweeper(scopeFactory, null!, _time, logger);
        var withoutTime = () => new InboxSweeper(scopeFactory, options, null!, logger);
        var withoutLogger = () => new InboxSweeper(scopeFactory, options, _time, null!);

        withoutScopes.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void The_pruner_is_registered_with_the_durable_inbox()
    {
        _provider.GetServices<IHostedService>().OfType<InboxSweeper>().Should().ContainSingle();
    }
}
