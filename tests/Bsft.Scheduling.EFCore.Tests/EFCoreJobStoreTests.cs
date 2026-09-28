using Bsft.Scheduling.EFCore.Tests.Fakes;
using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// Job run persistence against SQLite over a single shared open connection: the EF InMemory provider
/// ignores both the concurrency token and the filtered unique index, which are the two mechanisms the
/// store's correctness rests on.
/// </summary>
public class EFCoreJobStoreTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly EFCoreJobStore _store;

    public EFCoreJobStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IJobDbContext>(sp => sp.GetRequiredService<TestJobDbContext>());
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<TestJobDbContext>().Database.EnsureCreated();

        _store = new EFCoreJobStore(_provider.GetRequiredService<IServiceScopeFactory>(), new JobRunMapper());
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        ((Action)(() => new EFCoreJobStore(null!, new JobRunMapper())))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => new EFCoreJobStore(scopeFactory, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("mapper");
    }

    // ---- EnqueueAsync -------------------------------------------------------------------------

    [Fact]
    public async Task EnqueueAsyncStoresTheRunAndStampsTheIdentityBackOntoTheCaller()
    {
        var run = Run("report");

        var id = await _store.EnqueueAsync(run);

        id.Should().BeGreaterThan(0);
        run.Id.Should().Be(id, "the caller has to be able to cancel the run without a round trip");
        run.ConcurrencyToken.Should().NotBeEmpty();

        var stored = await _store.FindAsync(id);
        stored.Should().NotBeNull();
        stored!.JobName.Should().Be("report");
        stored.ConcurrencyToken.Should().Be(run.ConcurrencyToken);
    }

    [Fact]
    public async Task EnqueueAsyncRejectsNull()
    {
        var act = async () => await _store.EnqueueAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    // ---- TryEnqueueRecurringAsync -------------------------------------------------------------

    [Theory]
    [InlineData(JobOverlapPolicy.Forbid)]
    [InlineData(JobOverlapPolicy.Allow)]
    public async Task TryEnqueueRecurringAsyncStoresTheFirstOccurrence(JobOverlapPolicy overlapPolicy)
    {
        var run = Recurring("cleanup");

        var stored = await _store.TryEnqueueRecurringAsync(run, overlapPolicy);

        stored.Should().BeTrue();
        run.Id.Should().BeGreaterThan(0);
        run.ConcurrencyToken.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(JobOverlapPolicy.Forbid)]
    [InlineData(JobOverlapPolicy.Allow)]
    public async Task APendingOccurrenceBlocksASecondOneUnderEitherOverlapPolicy(JobOverlapPolicy overlapPolicy)
    {
        await _store.TryEnqueueRecurringAsync(Recurring("cleanup"), overlapPolicy);

        (await _store.TryEnqueueRecurringAsync(Recurring("cleanup"), overlapPolicy)).Should().BeFalse();
    }

    [Fact]
    public async Task ARunningOccurrenceBlocksTheNextOneWhenOverlapIsForbidden()
    {
        var first = Recurring("cleanup");
        await _store.TryEnqueueRecurringAsync(first, JobOverlapPolicy.Forbid);
        var claimed = await _store.ClaimAsync(first, "instance-1", _now, _now.AddMinutes(5));
        claimed.Should().NotBeNull();

        (await _store.TryEnqueueRecurringAsync(Recurring("cleanup"), JobOverlapPolicy.Forbid)).Should().BeFalse();
    }

    [Fact]
    public async Task ARunningOccurrenceDoesNotBlockTheNextOneWhenOverlapIsAllowed()
    {
        var first = Recurring("cleanup");
        await _store.TryEnqueueRecurringAsync(first, JobOverlapPolicy.Allow);
        var claimed = await _store.ClaimAsync(first, "instance-1", _now, _now.AddMinutes(5));
        claimed.Should().NotBeNull();

        // Keeping the schedule ticking while a slow run is still going is the whole point of Allow,
        // so the successor has to be accepted by the check and by the database.
        var successor = Recurring("cleanup");
        (await _store.TryEnqueueRecurringAsync(successor, JobOverlapPolicy.Allow)).Should().BeTrue();
        successor.Id.Should().NotBe(first.Id);
    }

    [Theory]
    [InlineData(JobRunStatus.Completed)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.Skipped)]
    public async Task TryEnqueueRecurringAsyncAllowsANewOccurrenceOnceThePreviousOneSettled(JobRunStatus terminal)
    {
        var first = Recurring("cleanup");
        await _store.TryEnqueueRecurringAsync(first, JobOverlapPolicy.Forbid);
        var claimed = await _store.ClaimAsync(first, "instance-1", _now, _now.AddMinutes(5));
        await _store.SettleAsync(claimed!, terminal, _now.AddMinutes(1));

        (await _store.TryEnqueueRecurringAsync(Recurring("cleanup"), JobOverlapPolicy.Forbid)).Should().BeTrue();
    }

    [Fact]
    public async Task TryEnqueueRecurringAsyncRejectsNull()
    {
        var act = async () => await _store.TryEnqueueRecurringAsync(null!, JobOverlapPolicy.Forbid);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    [Fact]
    public async Task TryEnqueueRecurringAsyncReturnsFalseWhenTheUniqueIndexRejectsTheInsert()
    {
        // The competing occurrence appears after the existence check has already passed, which is the
        // race the unique index exists for. The store must read the rejected insert as "it is there
        // already", not as a failure.
        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IJobDbContext>(sp => new RacingJobDbContext(
            sp.GetRequiredService<TestJobDbContext>(),
            () => InsertPendingRecurringRowAsync("cleanup")));
        using var provider = services.BuildServiceProvider();

        var store = new EFCoreJobStore(provider.GetRequiredService<IServiceScopeFactory>(), new JobRunMapper());

        var stored = await store.TryEnqueueRecurringAsync(Recurring("cleanup"), JobOverlapPolicy.Forbid);

        stored.Should().BeFalse();
        (await CountRowsAsync("cleanup")).Should().Be(1, "the loser's row must not have been written");
    }

    [Fact]
    public async Task TryEnqueueRecurringAsyncRethrowsASaveFailureThatIsNotTheOccurrenceRace()
    {
        // A save can fail for reasons that have nothing to do with a competing instance — a name past
        // the column length, a schema that does not match the model. Reading those as "it is there
        // already" would leave the recurring job with no occurrence, no exception and nothing in the
        // log, and the seeder would report a success it never had.
        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IJobDbContext>(sp => new FailingSaveJobDbContext(
            sp.GetRequiredService<TestJobDbContext>()));
        using var provider = services.BuildServiceProvider();

        var store = new EFCoreJobStore(provider.GetRequiredService<IServiceScopeFactory>(), new JobRunMapper());

        var act = async () => await store.TryEnqueueRecurringAsync(Recurring("cleanup"), JobOverlapPolicy.Forbid);

        await act.Should().ThrowAsync<DbUpdateException>();
        (await CountRowsAsync("cleanup")).Should().Be(0, "the occurrence the caller was promised does not exist");
    }

    // ---- ListClaimableAsync --------------------------------------------------------------------

    [Fact]
    public async Task GetClaimableAsyncReturnsPendingRunsThatAreDue()
    {
        await _store.EnqueueAsync(Run("due", _now.AddMinutes(-1)));

        var claimable = await _store.ListClaimableAsync(_now, 10);

        claimable.Should().ContainSingle().Which.JobName.Should().Be("due");
    }

    [Fact]
    public async Task GetClaimableAsyncExcludesPendingRunsThatAreNotDueYet()
    {
        await _store.EnqueueAsync(Run("later", _now.AddMinutes(1)));

        (await _store.ListClaimableAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetClaimableAsyncIncludesARunningRunWhoseLeaseHasExpired()
    {
        var run = Run("crashed", _now.AddMinutes(-10));
        await _store.EnqueueAsync(run);
        await _store.ClaimAsync(run, "instance-1", _now.AddMinutes(-10), _now.AddMinutes(-5));

        // The instance that held the lease went away, so the run has to become claimable again.
        var claimable = await _store.ListClaimableAsync(_now, 10);

        claimable.Should().ContainSingle().Which.Status.Should().Be(JobRunStatus.Running);
    }

    [Fact]
    public async Task GetClaimableAsyncExcludesARunningRunWithALiveLease()
    {
        var run = Run("busy", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        await _store.ClaimAsync(run, "instance-1", _now, _now.AddMinutes(5));

        (await _store.ListClaimableAsync(_now, 10)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(JobRunStatus.Completed)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.Skipped)]
    public async Task GetClaimableAsyncExcludesSettledRuns(JobRunStatus terminal)
    {
        var run = Run("done", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var claimed = await _store.ClaimAsync(run, "instance-1", _now, _now.AddMinutes(5));
        await _store.SettleAsync(claimed!, terminal, _now);

        (await _store.ListClaimableAsync(_now, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetClaimableAsyncHonoursTheBatchSize()
    {
        await _store.EnqueueAsync(Run("a", _now.AddMinutes(-3)));
        await _store.EnqueueAsync(Run("b", _now.AddMinutes(-2)));
        await _store.EnqueueAsync(Run("c", _now.AddMinutes(-1)));

        var claimable = await _store.ListClaimableAsync(_now, 2);

        claimable.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetClaimableAsyncReturnsTheOldestDueRunFirstAndBreaksTiesByIdentity()
    {
        await _store.EnqueueAsync(Run("second", _now));
        await _store.EnqueueAsync(Run("third", _now));
        await _store.EnqueueAsync(Run("first", _now.AddMinutes(-5)));

        var claimable = await _store.ListClaimableAsync(_now, 10);

        claimable.Select(r => r.JobName).Should().ContainInOrder("first", "second", "third");
    }

    // ---- ClaimAsync ------------------------------------------------------------------------

    [Fact]
    public async Task ClaimAsyncTakesTheLeaseAndRollsTheConcurrencyTokenForward()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var polled = (await _store.ListClaimableAsync(_now, 10))[0];

        var claimed = await _store.ClaimAsync(polled, "instance-1", _now, _now.AddMinutes(5));

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(JobRunStatus.Running);
        claimed.AttemptCount.Should().Be(1);
        claimed.LeaseOwner.Should().Be("instance-1");
        claimed.LeaseExpiresAt.Should().Be(_now.AddMinutes(5));
        claimed.ConcurrencyToken.Should().NotBe(polled.ConcurrencyToken);

        var stored = await _store.FindAsync(run.Id);
        stored!.Status.Should().Be(JobRunStatus.Running);
        stored.AttemptCount.Should().Be(1);
        stored.LeaseOwner.Should().Be("instance-1");
        stored.ConcurrencyToken.Should().Be(claimed.ConcurrencyToken);
    }

    [Fact]
    public async Task AClaimedRunCannotBeClaimedAgainByAnotherInstance()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var polled = (await _store.ListClaimableAsync(_now, 10))[0];

        var first = await _store.ClaimAsync(polled, "instance-1", _now, _now.AddMinutes(5));

        // The second instance polled before the claim and still carries the pre-claim token. This is
        // the guard against executing one occurrence twice.
        var second = await _store.ClaimAsync(polled, "instance-2", _now, _now.AddMinutes(5));

        first.Should().NotBeNull();
        second.Should().BeNull("a stale token must never win a claim");
        (await _store.FindAsync(run.Id))!.LeaseOwner.Should().Be("instance-1");
    }

    [Fact]
    public async Task ClaimAsyncReturnsNullForARunThatWasCancelledInBetween()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var polled = (await _store.ListClaimableAsync(_now, 10))[0];

        await _store.CancelAsync(run.Id);

        (await _store.ClaimAsync(polled, "instance-1", _now, _now.AddMinutes(5))).Should().BeNull();
    }

    [Fact]
    public async Task ClaimAsyncRejectsNullArguments()
    {
        await FluentActions.Awaiting(() => _store.ClaimAsync(null!, "instance-1", _now, _now.AddMinutes(5)))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
        await FluentActions.Awaiting(() => _store.ClaimAsync(Run("report"), null!, _now, _now.AddMinutes(5)))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("owner");
    }

    // ---- SettleAsync --------------------------------------------------------------------------

    [Theory]
    [InlineData(JobRunStatus.Completed)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.Skipped)]
    public async Task SettleAsyncWritesTheTerminalStatusAndReleasesTheLease(JobRunStatus terminal)
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var claimed = await _store.ClaimAsync(run, "instance-1", _now, _now.AddMinutes(5));

        await _store.SettleAsync(claimed!, terminal, _now.AddMinutes(2));

        var stored = await _store.FindAsync(run.Id);
        stored!.Status.Should().Be(terminal);
        stored.SettledAt.Should().Be(_now.AddMinutes(2));
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task SettleAsyncWritesNothingOnceTheLeaseHasBeenLost()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var polled = (await _store.ListClaimableAsync(_now, 10))[0];
        await _store.ClaimAsync(polled, "instance-1", _now, _now.AddMinutes(5));

        // The loser of the claim race must not be able to settle the run the winner is executing.
        await _store.SettleAsync(polled, JobRunStatus.Completed, _now.AddMinutes(2));

        var stored = await _store.FindAsync(run.Id);
        stored!.Status.Should().Be(JobRunStatus.Running);
        stored.SettledAt.Should().BeNull();
        stored.LeaseOwner.Should().Be("instance-1");
    }

    [Fact]
    public async Task SettleAsyncRejectsNull()
    {
        var act = async () => await _store.SettleAsync(null!, JobRunStatus.Completed, _now);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    // ---- RetryAsync ---------------------------------------------------------------------------

    [Fact]
    public async Task RetryAsyncReturnsTheRunToPendingKeepingItsAttemptCount()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var claimed = await _store.ClaimAsync(run, "instance-1", _now, _now.AddMinutes(5));

        await _store.RetryAsync(claimed!, _now.AddMinutes(10));

        var stored = await _store.FindAsync(run.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(10));
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.AttemptCount.Should().Be(1, "the attempt count is what eventually exhausts the retries");
    }

    [Fact]
    public async Task RetryAsyncWritesNothingOnceTheLeaseHasBeenLost()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        var polled = (await _store.ListClaimableAsync(_now, 10))[0];
        await _store.ClaimAsync(polled, "instance-1", _now, _now.AddMinutes(5));

        await _store.RetryAsync(polled, _now.AddMinutes(10));

        var stored = await _store.FindAsync(run.Id);
        stored!.Status.Should().Be(JobRunStatus.Running);
        stored.LeaseOwner.Should().Be("instance-1");
    }

    [Fact]
    public async Task RetryAsyncGivesUpTheOccurrenceSlotWhenTheSuccessorAlreadyHoldsIt()
    {
        // Under JobOverlapPolicy.Allow the successor is created before the work starts, so by the time
        // a failed attempt is retried the single pending-occurrence slot is already taken. Putting the
        // run back as a pending recurring row would be rejected by the unique filtered index, and the
        // exception would unwind through the whole dispatch pass.
        var first = Recurring("cleanup");
        await _store.TryEnqueueRecurringAsync(first, JobOverlapPolicy.Allow);
        var claimed = await _store.ClaimAsync(first, "instance-1", _now, _now.AddMinutes(5));
        (await _store.TryEnqueueRecurringAsync(Recurring("cleanup"), JobOverlapPolicy.Allow)).Should().BeTrue();

        var act = async () => await _store.RetryAsync(claimed!, _now.AddMinutes(10));

        await act.Should().NotThrowAsync();

        var stored = await _store.FindAsync(first.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(10));
        stored.AttemptCount.Should().Be(1, "the retry semantics are unchanged; only the slot is released");
        stored.IsRecurring.Should().BeFalse("the successor carries the schedule from here on");
    }

    [Fact]
    public async Task RetryAsyncKeepsTheOccurrenceSlotWhileNoSuccessorExists()
    {
        // Under JobOverlapPolicy.Forbid the successor is only created once the run has settled, so the
        // retry is still the job's pending occurrence and has to go on blocking a duplicate.
        var first = Recurring("cleanup");
        await _store.TryEnqueueRecurringAsync(first, JobOverlapPolicy.Forbid);
        var claimed = await _store.ClaimAsync(first, "instance-1", _now, _now.AddMinutes(5));

        await _store.RetryAsync(claimed!, _now.AddMinutes(10));

        (await _store.FindAsync(first.Id))!.IsRecurring.Should().BeTrue();
        (await _store.TryEnqueueRecurringAsync(Recurring("cleanup"), JobOverlapPolicy.Forbid)).Should().BeFalse();
    }

    [Fact]
    public async Task RetryAsyncSurvivesASuccessorThatAppearsWhileTheRetryIsBeingWritten()
    {
        // Under Forbid no successor exists while the run is executing, so the retry is entitled to keep
        // the occurrence slot. The run is Running, which the filtered unique index does not cover, so
        // the periodic re-seed on another instance sees no pending occurrence and creates one in the
        // window between the store's successor check and its update. The update then collides with it.
        // Letting that collision out would abort the whole dispatch pass and leave the row Running with
        // an attempt charged until its lease lapsed.
        var first = Recurring("cleanup");
        await _store.TryEnqueueRecurringAsync(first, JobOverlapPolicy.Forbid);
        var claimed = await _store.ClaimAsync(first, "instance-1", _now, _now.AddMinutes(5));

        var services = new ServiceCollection();
        services.AddDbContext<TestJobDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IJobDbContext>(sp => new RetryRacingJobDbContext(
            sp.GetRequiredService<TestJobDbContext>(),
            () => InsertPendingRecurringRowAsync("cleanup")));
        using var provider = services.BuildServiceProvider();

        var store = new EFCoreJobStore(provider.GetRequiredService<IServiceScopeFactory>(), new JobRunMapper());

        var act = async () => await store.RetryAsync(claimed!, _now.AddMinutes(10));

        await act.Should().NotThrowAsync();

        var stored = await _store.FindAsync(first.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(10));
        stored.AttemptCount.Should().Be(1, "the retry semantics are unchanged; only the slot is released");
        stored.IsRecurring.Should().BeFalse("the winner of the race carries the schedule from here on");
        (await CountRowsAsync("cleanup")).Should().Be(2);
    }

    [Fact]
    public async Task RetryAsyncRejectsNull()
    {
        var act = async () => await _store.RetryAsync(null!, _now);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    // ---- CancelAsync --------------------------------------------------------------------------

    [Fact]
    public async Task CancelAsyncRemovesAPendingRun()
    {
        var run = Run("report", _now.AddMinutes(5));
        await _store.EnqueueAsync(run);

        (await _store.CancelAsync(run.Id)).Should().BeTrue();
        (await _store.FindAsync(run.Id)).Should().BeNull();
    }

    [Fact]
    public async Task CancelAsyncRefusesToRemoveARunningRun()
    {
        var run = Run("report", _now.AddMinutes(-1));
        await _store.EnqueueAsync(run);
        await _store.ClaimAsync(run, "instance-1", _now, _now.AddMinutes(5));

        // Deleting it would strand the instance that holds the lease.
        (await _store.CancelAsync(run.Id)).Should().BeFalse();
        (await _store.FindAsync(run.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task CancelAsyncReturnsFalseForAnUnknownIdentity()
    {
        (await _store.CancelAsync(4711)).Should().BeFalse();
    }

    // ---- GetAsync -----------------------------------------------------------------------------

    [Fact]
    public async Task GetAsyncRoundTripsEveryField()
    {
        var run = new JobRun
        {
            JobName = "report",
            DueAt = _now,
            Status = JobRunStatus.Failed,
            IsRecurring = true,
            Payload = "{\"tenant\":1}",
            CorrelationId = "corr-9",
            AttemptCount = 2,
            LeaseOwner = "instance-1",
            LeaseExpiresAt = _now.AddMinutes(5),
            SettledAt = _now.AddMinutes(7),
        };

        var id = await _store.EnqueueAsync(run);
        var stored = await _store.FindAsync(id);

        stored.Should().NotBeNull();
        stored!.Id.Should().Be(id);
        stored.JobName.Should().Be("report");
        stored.DueAt.Should().Be(_now);
        stored.Status.Should().Be(JobRunStatus.Failed);
        stored.IsRecurring.Should().BeTrue();
        stored.Payload.Should().Be("{\"tenant\":1}");
        stored.CorrelationId.Should().Be("corr-9");
        stored.AttemptCount.Should().Be(2);
        stored.LeaseOwner.Should().Be("instance-1");
        stored.LeaseExpiresAt.Should().Be(_now.AddMinutes(5));
        stored.SettledAt.Should().Be(_now.AddMinutes(7));
        stored.ConcurrencyToken.Should().Be(run.ConcurrencyToken);
    }

    [Fact]
    public async Task GetAsyncRoundTripsTheOptionalFieldsAsNull()
    {
        var id = await _store.EnqueueAsync(Run("report"));

        var stored = await _store.FindAsync(id);

        stored!.Payload.Should().BeNull();
        stored.CorrelationId.Should().BeNull();
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
        stored.SettledAt.Should().BeNull();
    }

    [Fact]
    public async Task GetAsyncReturnsInstantsAtZeroOffset()
    {
        var run = Run("report");
        run.LeaseExpiresAt = _now.AddMinutes(5);
        run.SettledAt = _now.AddMinutes(7);
        var id = await _store.EnqueueAsync(run);

        var stored = await _store.FindAsync(id);

        // The provider drops the kind on the way out, so the store has to reinstate UTC rather than
        // let the host's local offset creep in.
        stored!.DueAt.Offset.Should().Be(TimeSpan.Zero);
        stored.DueAt.Should().Be(_now);
        stored.LeaseExpiresAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        stored.LeaseExpiresAt.Value.Should().Be(_now.AddMinutes(5));
        stored.SettledAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        stored.SettledAt.Value.Should().Be(_now.AddMinutes(7));
    }

    [Fact]
    public async Task GetAsyncReturnsNullForAnUnknownIdentity()
    {
        (await _store.FindAsync(4711)).Should().BeNull();
    }

    [Fact]
    public async Task ARunSurvivesAProcessRestart()
    {
        var id = await _store.EnqueueAsync(Run("report", _now.AddDays(2)));

        // A fresh store on the same database stands in for the restarted process.
        var afterRestart = new EFCoreJobStore(_provider.GetRequiredService<IServiceScopeFactory>(), new JobRunMapper());

        var stored = await afterRestart.FindAsync(id);
        stored.Should().NotBeNull();
        stored!.DueAt.Should().Be(_now.AddDays(2));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static JobRun Run(string jobName = "report", DateTimeOffset? dueAt = null) => new()
    {
        JobName = jobName,
        DueAt = dueAt ?? _now,
        Status = JobRunStatus.Pending,
    };

    private static JobRun Recurring(string jobName) => new()
    {
        JobName = jobName,
        DueAt = _now,
        Status = JobRunStatus.Pending,
        IsRecurring = true,
    };

    private DbContextOptions<TestJobDbContext> ContextOptions() =>
        new DbContextOptionsBuilder<TestJobDbContext>().UseSqlite(_connection).Options;

    private async Task InsertPendingRecurringRowAsync(string jobName)
    {
        await using var db = new TestJobDbContext(ContextOptions());
        db.JobRuns.Add(new JobRunEntity
        {
            JobName = jobName,
            DueAt = _now.UtcDateTime,
            Status = JobRunStatus.Pending,
            IsRecurring = true,
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> CountRowsAsync(string jobName)
    {
        await using var db = new TestJobDbContext(ContextOptions());
        return await db.JobRuns.AsNoTracking().CountAsync(e => e.JobName == jobName);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
