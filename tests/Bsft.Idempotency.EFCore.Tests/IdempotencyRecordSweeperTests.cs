using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Idempotency.EFCore.Tests.Fakes;
using Bsft.Idempotency.EFCore.Tests.Support;

namespace Bsft.Idempotency.EFCore.Tests;

public sealed class IdempotencyRecordSweeperTests : IDisposable
{
    private const string Scope = "scope_acme";

    /// <summary>What the pruner bounds one statement at, mirrored so the test says what it asserts.</summary>
    private const int BatchSize = 500;

    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteFixture _sqlite = new();
    private readonly MutableTimeProvider _time = new(Now);
    private readonly ServiceProvider _provider;

    public IdempotencyRecordSweeperTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyDbContext>(_ => _sqlite.NewContext());

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _sqlite.Dispose();
    }

    private IdempotencyRecordSweeper Pruner(
        TimeSpan? retention = null,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<IdempotencyRecordSweeper>? logger = null) =>
        new(scopeFactory ?? _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BsftIdempotencyOptions { Retention = retention ?? TimeSpan.FromHours(24) }),
            _time,
            logger ?? NullLogger<IdempotencyRecordSweeper>.Instance);

    /// <summary>Seeds a backlog of expired records in one save.</summary>
    /// <param name="count">How many.</param>
    /// <returns>A task that completes when they are stored.</returns>
    private async Task BacklogAsync(int count)
    {
        await using var context = _sqlite.NewContext();

        for (var index = 0; index < count; index++)
        {
            context.IdempotencyRecords.Add(new IdempotencyRecordEntity
            {
                Scope = Scope,
                Key = "key_backlog_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Fingerprint = "fp",
                State = IdempotencyRecordState.Completed,
                ExpiresAt = (Now - TimeSpan.FromHours(1)).UtcDateTime,
            });
        }

        await context.SaveChangesAsync();
    }

    private async Task RecordAsync(string key, IdempotencyRecordState state, TimeSpan expiresIn)
    {
        await using var context = _sqlite.NewContext();

        context.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            Scope = Scope,
            Key = key,
            Fingerprint = "fp",
            State = state,
            ExpiresAt = (Now + expiresIn).UtcDateTime,
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Reclaims the lapsed record through a second connection, the way another replica would.</summary>
    /// <returns>A task that completes when the reclaim is committed.</returns>
    private async Task ReclaimThroughAnotherConnectionAsync()
    {
        await using var context = _sqlite.NewContext();

        await context.IdempotencyRecords
            .Where(entity => entity.Scope == Scope)
            .ExecuteUpdateAsync(set => set.SetProperty(
                entity => entity.ExpiresAt, (Now + TimeSpan.FromMinutes(5)).UtcDateTime));
    }

    private async Task<List<IdempotencyRecordEntity>> ReadRecordsAsync()
    {
        await using var context = _sqlite.NewContext();

        return await context.IdempotencyRecords.AsNoTracking().OrderBy(entity => entity.Id).ToListAsync();
    }

    [Fact]
    public async Task Only_the_records_that_have_stopped_deciding_anything_are_pruned()
    {
        await RecordAsync("key_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("key_fresh", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));

        var pruned = await Pruner().PurgeOnceAsync();

        pruned.Should().Be(1);
        (await ReadRecordsAsync()).Should().ContainSingle().Which.Key.Should().Be("key_fresh");
    }

    [Fact]
    public async Task A_backlog_larger_than_one_batch_is_drained_a_batch_at_a_time()
    {
        // The table takes a row per protected request from every replica, so a first sweep can meet a
        // backlog of any size. One unbounded delete against a table that is simultaneously taking an
        // insert for every live request holds locks for as long as it runs; a bounded one does not,
        // and the pass that filled its batch asks for the next one straight away.
        await BacklogAsync(BatchSize + 1);

        var first = await Pruner().PurgeOnceAsync();
        var second = await Pruner().PurgeOnceAsync();

        first.Should().Be(BatchSize);
        second.Should().Be(1);
        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_record_reclaimed_between_the_read_and_the_delete_is_left_standing()
    {
        // The page of ids is read in one statement and deleted in the next, and a request reclaiming a
        // lapsed key in that window writes a lease in the future onto one of those very rows. Deleting
        // it by id alone would free a key a request is using, so the delete carries the expiry too.
        await RecordAsync("key_reclaimed", IdempotencyRecordState.Reserved, -TimeSpan.FromMinutes(1));

        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyDbContext>(
            _ => _sqlite.NewContextWith(new RunBeforeTheDelete(ReclaimThroughAnotherConnectionAsync)));

        using var provider = services.BuildServiceProvider();

        var pruned = await Pruner(scopeFactory: provider.GetRequiredService<IServiceScopeFactory>())
            .PurgeOnceAsync();

        pruned.Should().Be(0);
        (await ReadRecordsAsync()).Should().ContainSingle()
            .Which.ExpiresAt.Should().Be((Now + TimeSpan.FromMinutes(5)).UtcDateTime);
    }

    [Fact]
    public async Task A_record_exactly_at_its_expiry_is_kept()
    {
        // The boundary belongs to the record: the expiry is the instant it stops deciding, and the
        // predicate is strict so a sweep landing on it does not race the request reading it.
        await RecordAsync("key_edge", IdempotencyRecordState.Completed, TimeSpan.Zero);

        var pruned = await Pruner().PurgeOnceAsync();

        pruned.Should().Be(0);
        (await ReadRecordsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_reservation_still_inside_its_lease_is_never_pruned()
    {
        // The property the whole single-column design exists for: a sweep must not free a key a request
        // is still using, or the retry it lets through runs the work a second time.
        await RecordAsync("key_running", IdempotencyRecordState.Reserved, TimeSpan.FromSeconds(30));

        var pruned = await Pruner().PurgeOnceAsync();

        pruned.Should().Be(0);
        (await ReadRecordsAsync()).Should().ContainSingle()
            .Which.State.Should().Be(IdempotencyRecordState.Reserved);
    }

    [Fact]
    public async Task A_reservation_whose_lease_lapsed_is_pruned_like_anything_else()
    {
        // Abandoned is not a stored state: it is what a reserved record past its lease is, and nothing
        // is left to write it because the process that would have is the one that crashed.
        await RecordAsync("key_abandoned", IdempotencyRecordState.Reserved, -TimeSpan.FromMinutes(1));

        var pruned = await Pruner().PurgeOnceAsync();

        pruned.Should().Be(1);
        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_loop_prunes_what_has_expired_and_stops_when_it_is_asked_to()
    {
        await RecordAsync("key_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));
        await RecordAsync("key_fresh", IdempotencyRecordState.Completed, TimeSpan.FromHours(1));

        await RunUntilSweptAsync(logger => Pruner(logger: logger));

        (await ReadRecordsAsync()).Should().ContainSingle().Which.Key.Should().Be("key_fresh");
    }

    [Fact]
    public async Task A_retention_shorter_than_the_sweep_cap_sets_the_cadence_itself()
    {
        // The cadence follows the retention where that is the shorter of the two, so a short retention
        // is kept honest without a second option to configure.
        await RecordAsync("key_stale", IdempotencyRecordState.Completed, -TimeSpan.FromMinutes(1));

        await RunUntilSweptAsync(logger => Pruner(retention: TimeSpan.FromMinutes(1), logger: logger));

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_retention_longer_than_the_sweep_cap_still_sweeps()
    {
        // The cap keeps a long retention from becoming a rare, enormous delete, so a retention of days
        // must not mean the first sweep is days away.
        await RecordAsync("key_ancient", IdempotencyRecordState.Completed, -TimeSpan.FromDays(30));

        await RunUntilSweptAsync(logger => Pruner(retention: TimeSpan.FromDays(7), logger: logger));

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_sweep_that_failed_does_not_take_the_service_down()
    {
        // A database that is briefly unreachable must not stop the pruning for the life of the process;
        // the rows a failed sweep left behind are simply that much older when the next one runs.
        using var emptyProvider = new ServiceCollection().BuildServiceProvider();

        await ASweepThatWentWrongIsSurvivedAsync(emptyProvider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task A_sweep_cancelled_on_a_token_of_its_own_does_not_take_the_service_down()
    {
        // The command-timeout shape. Read as a shutdown it would escape the loop unhandled and take the
        // host down over one slow statement, so it has to count as a failed sweep instead.
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyDbContext, SelfCancelingIdempotencyDbContext>();
        using var cancelling = services.BuildServiceProvider();

        await ASweepThatWentWrongIsSurvivedAsync(cancelling.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task Starting_it_as_a_hosted_service_sweeps_on_its_own()
    {
        await RecordAsync("key_stale", IdempotencyRecordState.Completed, -TimeSpan.FromHours(1));

        var swept = new SignallingLogger<IdempotencyRecordSweeper>(LogLevel.Information);
        var pruner = Pruner(logger: swept);

        await pruner.StartAsync(CancellationToken.None);

        // WhenAny against the service's own task rather than a bare await, so a sweep that never happens
        // fails this test instead of hanging it.
        await Task.WhenAny(swept.Reached, pruner.ExecuteTask!);
        await pruner.StopAsync(CancellationToken.None);

        swept.Reached.IsCompletedSuccessfully.Should().BeTrue();
        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Runs the loop until its first sweep has deleted something, then stops it and waits for it to
    /// unwind.
    /// </summary>
    /// <remarks>
    /// The stop is driven by the sweep reporting what it pruned, which the pruner does only after the
    /// delete has returned, rather than by a timer the loop is hoped to beat. So the table the assertion
    /// then reads is one the loop is finished with however slow or busy the machine is, and the wait has
    /// no duration in it to be wrong about.
    /// </remarks>
    /// <param name="create">Creates the pruner under test around the logger that signals the sweep.</param>
    /// <returns>A task that completes once the loop has swept and unwound.</returns>
    private static async Task RunUntilSweptAsync(
        Func<ILogger<IdempotencyRecordSweeper>, IdempotencyRecordSweeper> create)
    {
        var swept = new SignallingLogger<IdempotencyRecordSweeper>(LogLevel.Information);
        using var cts = new CancellationTokenSource();

        var loop = create(swept).RunLoopAsync(cts.Token);

        await Task.WhenAny(swept.Reached, loop);
        swept.Reached.IsCompletedSuccessfully.Should().BeTrue();

        await cts.CancelAsync();
        await loop;
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
    /// <param name="scopeFactory">The scopes whose context makes the sweep go wrong.</param>
    /// <returns>A task that completes once the loop has reported the failure and unwound.</returns>
    private async Task ASweepThatWentWrongIsSurvivedAsync(IServiceScopeFactory scopeFactory)
    {
        var reported = new SignallingLogger<IdempotencyRecordSweeper>(LogLevel.Error);
        using var cts = new CancellationTokenSource();

        var loop = Pruner(scopeFactory: scopeFactory, logger: reported).RunLoopAsync(cts.Token);

        await Task.WhenAny(reported.Reached, loop);
        await cts.CancelAsync();

        var act = async () => await loop;

        await act.Should().NotThrowAsync();
        reported.Reached.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new BsftIdempotencyOptions());
        var logger = NullLogger<IdempotencyRecordSweeper>.Instance;

        var withoutScopes = () => new IdempotencyRecordSweeper(null!, options, _time, logger);
        var withoutOptions = () => new IdempotencyRecordSweeper(scopeFactory, null!, _time, logger);
        var withoutTime = () => new IdempotencyRecordSweeper(scopeFactory, options, null!, logger);
        var withoutLogger = () => new IdempotencyRecordSweeper(scopeFactory, options, _time, null!);

        withoutScopes.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
