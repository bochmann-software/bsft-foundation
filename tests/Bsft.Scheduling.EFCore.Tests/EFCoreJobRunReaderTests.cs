using Bsft.Data.Contracts.Pagination;
using Bsft.Scheduling.EFCore.Tests.Support;
using Bsft.Scheduling.Reading;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

/// <summary>
/// The operator read side against SQLite rather than the EF InMemory provider, because the prefix
/// criterion is a LIKE with an escape character and the orders put nulls last, and neither is a
/// question the in-memory provider can answer the way a database does.
/// </summary>
public class EFCoreJobRunReaderTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly TestJobDbContext _db;
    private readonly EFCoreJobRunReader _reader;

    public EFCoreJobRunReaderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new TestJobDbContext(
            new DbContextOptionsBuilder<TestJobDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _reader = new EFCoreJobRunReader(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheConstructorRejectsAMissingContext() =>
        ((Action)(() => new EFCoreJobRunReader(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("db");

    [Fact]
    public async Task ListAsyncRejectsAMissingPageRequest()
    {
        var act = async () => await _reader.ListAsync(null, JobRunOrder.DueDescending, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task ListAsyncWithoutAFilterAnswersEveryRunNewestDueFirst()
    {
        Store(Run("a", due: _now.AddHours(-3)), Run("b", due: _now.AddHours(-1)), Run("c", due: _now.AddHours(-2)));

        var page = await _reader.ListAsync(null, JobRunOrder.DueDescending, Request());

        page.TotalCount.Should().Be(3);
        page.HasNextPage.Should().BeFalse();
        page.Items.Select(item => item.JobName).Should().ContainInOrder("b", "c", "a");
    }

    [Fact]
    public async Task ListAsyncAnswersTheRunAsTheListingShowsIt()
    {
        Store(Run(
            "nightly-import",
            due: _now.AddHours(-1),
            status: JobRunStatus.Failed,
            recurring: true,
            correlationId: "corr-1",
            attempts: 3,
            owner: "worker-2",
            leaseExpires: _now.AddMinutes(-5),
            settled: _now));

        var summary = (await _reader.ListAsync(null, JobRunOrder.DueDescending, Request())).Items.Single();

        summary.Id.Should().BeGreaterThan(0);
        summary.JobName.Should().Be("nightly-import");
        summary.DueAt.Should().Be(_now.AddHours(-1));
        summary.Status.Should().Be(JobRunStatus.Failed);
        summary.IsRecurring.Should().BeTrue();
        summary.CorrelationId.Should().Be("corr-1");
        summary.AttemptCount.Should().Be(3);
        summary.LeaseOwner.Should().Be("worker-2");
        summary.LeaseExpiresAt.Should().Be(_now.AddMinutes(-5));
        summary.SettledAt.Should().Be(_now);
    }

    [Fact]
    public async Task ListAsyncNarrowsToEveryCriterionTheCallerNamed()
    {
        Store(
            Run("nightly-import", status: JobRunStatus.Failed, recurring: true, correlationId: "corr-1",
                attempts: 3, owner: "worker-2", settled: _now),
            Run("nightly-import", status: JobRunStatus.Completed, settled: _now),
            Run("hourly-sweep", status: JobRunStatus.Failed, settled: _now));

        var filter = new JobRunFilter(
            JobName: "nightly-import",
            Status: JobRunStatus.Failed,
            IsRecurring: true,
            CorrelationId: "corr-1",
            LeaseOwner: "worker-2",
            MinAttemptCount: 3);

        var page = await _reader.ListAsync(filter, JobRunOrder.DueDescending, Request());

        page.Items.Should().ContainSingle().Which.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task ListAsyncMatchesAPrefixLiterallyRatherThanAsAPattern()
    {
        Store(Run("night_ly"), Run("nightXly"), Run("hourly"));

        var page = await _reader.ListAsync(
            new JobRunFilter(JobNamePrefix: "night_"), JobRunOrder.DueDescending, Request());

        page.Items.Should().ContainSingle().Which.JobName.Should().Be("night_ly");
    }

    [Fact]
    public async Task ListAsyncMatchesAPrefixCarryingTheEscapeCharacterItself()
    {
        Store(Run(@"night\ly"), Run("nightXly"));

        var page = await _reader.ListAsync(
            new JobRunFilter(JobNamePrefix: @"night\"), JobRunOrder.DueDescending, Request());

        page.Items.Should().ContainSingle().Which.JobName.Should().Be(@"night\ly");
    }

    [Fact]
    public async Task ListAsyncNarrowsToTheWindowTheRunWasDueIn()
    {
        Store(Run("early", due: _now.AddHours(-5)), Run("inside", due: _now.AddHours(-3)),
            Run("late", due: _now.AddHours(-1)));

        var filter = new JobRunFilter(DueAfter: _now.AddHours(-4), DueBefore: _now.AddHours(-2));

        var page = await _reader.ListAsync(filter, JobRunOrder.DueDescending, Request());

        page.Items.Should().ContainSingle().Which.JobName.Should().Be("inside");
    }

    [Fact]
    public async Task ListAsyncExcludesARunThatNeverSettledFromASettlementWindow()
    {
        Store(Run("settled", settled: _now.AddMinutes(-30)), Run("running"));

        var after = await _reader.ListAsync(
            new JobRunFilter(SettledAfter: _now.AddHours(-1)), JobRunOrder.DueDescending, Request());
        var before = await _reader.ListAsync(
            new JobRunFilter(SettledBefore: _now), JobRunOrder.DueDescending, Request());

        after.Items.Should().ContainSingle().Which.JobName.Should().Be("settled");
        before.Items.Should().ContainSingle().Which.JobName.Should().Be("settled");
    }

    [Fact]
    public async Task ListAsyncOrdersByWhatTheCallerAskedFor()
    {
        Store(
            Run("a", due: _now.AddHours(-3), settled: _now.AddHours(-2), leaseExpires: _now.AddMinutes(20)),
            Run("b", due: _now.AddHours(-1), settled: _now, leaseExpires: _now.AddMinutes(10)),
            Run("c", due: _now.AddHours(-2)));

        (await Names(JobRunOrder.DueAscending)).Should().ContainInOrder("a", "c", "b");
        (await Names(JobRunOrder.SettledDescending)).Should().ContainInOrder("b", "a", "c");
        (await Names(JobRunOrder.SettledAscending)).Should().ContainInOrder("a", "b", "c");
        (await Names(JobRunOrder.LeaseExpiry)).Should().ContainInOrder("b", "a", "c");
    }

    [Fact]
    public async Task ListAsyncPagesTheResultAndSaysWhetherMoreFollows()
    {
        Store(Run("a", due: _now.AddHours(-3)), Run("b", due: _now.AddHours(-2)), Run("c", due: _now.AddHours(-1)));

        var first = await _reader.ListAsync(null, JobRunOrder.DueAscending, Request(1, 2));
        var second = await _reader.ListAsync(null, JobRunOrder.DueAscending, Request(2, 2));

        first.Items.Select(item => item.JobName).Should().ContainInOrder("a", "b");
        first.HasNextPage.Should().BeTrue();
        second.Items.Select(item => item.JobName).Should().ContainInOrder("c");
        second.HasNextPage.Should().BeFalse();
        second.Page.Should().Be(2);
        second.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task ListAsyncReadsANonsensicalPageAsTheFirstOne()
    {
        Store(Run("a"));

        var page = await _reader.ListAsync(null, JobRunOrder.DueDescending, Request(0, 0));

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(1);
        page.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task FindAsyncAnswersTheRunWithWhatItWasAskedToDo()
    {
        var run = Run("nightly-import", payload: """{"since":"2026-08-19"}""");
        Store(run);

        var view = await _reader.FindAsync(run.Id);

        view.Should().NotBeNull();
        view!.Summary.JobName.Should().Be("nightly-import");
        view.Payload.Should().Be("""{"since":"2026-08-19"}""");
    }

    [Fact]
    public async Task FindAsyncAnswersNothingForARunThatIsNotThere() =>
        (await _reader.FindAsync(4711)).Should().BeNull();

    // ---- helpers ------------------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> Names(JobRunOrder order) =>
        (await _reader.ListAsync(null, order, Request())).Items.Select(item => item.JobName).ToList();

    private static PagedRequest Request(int page = 1, int pageSize = 25) =>
        new(Page: page, PageSize: pageSize);

    private void Store(params JobRunEntity[] runs)
    {
        _db.JobRuns.AddRange(runs);
        _db.SaveChanges();
    }

    private static JobRunEntity Run(
        string jobName,
        DateTimeOffset? due = null,
        JobRunStatus status = JobRunStatus.Pending,
        bool recurring = false,
        string? payload = null,
        string? correlationId = null,
        int attempts = 0,
        string? owner = null,
        DateTimeOffset? leaseExpires = null,
        DateTimeOffset? settled = null) => new()
        {
            JobName = jobName,
            DueAt = (due ?? _now).UtcDateTime,
            Status = status,
            IsRecurring = recurring,
            Payload = payload,
            CorrelationId = correlationId,
            AttemptCount = attempts,
            LeaseOwner = owner,
            LeaseExpiresAt = leaseExpires?.UtcDateTime,
            SettledAt = settled?.UtcDateTime,
            ConcurrencyToken = Guid.NewGuid(),
        };
}
