using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class InMemoryJobStoreTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryJobStore _store = new();

    [Fact]
    public async Task EnqueueAssignsAnIdentityAndStampsItBackOntoTheCaller()
    {
        var run = Pending("report", _now);

        var id = await _store.EnqueueAsync(run);

        id.Should().BeGreaterThan(0);
        run.Id.Should().Be(id);
        run.ConcurrencyToken.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task AReadHandsOutACopySoACallerCannotRewriteTheStore()
    {
        var id = await _store.EnqueueAsync(Pending("report", _now));

        var read = await _store.FindAsync(id);
        read!.JobName = "tampered";

        var reread = await _store.FindAsync(id);
        reread!.JobName.Should().Be("report");
    }

    [Fact]
    public async Task AReadNeverObservesARunHalfwayThroughAClaimOrARetry()
    {
        // Claim and retry rewrite a stored run field by field. A read taken outside the gate is free
        // to land between those writes and hand out a run whose status and lease contradict each
        // other — a row no database-backed store could ever produce, and one the dispatcher would
        // then act on.
        var id = await _store.EnqueueAsync(Pending("report", _now));
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var current = await _store.FindAsync(id);
                var claimed = await _store.ClaimAsync(current!, "instance-a", _now, _now.AddMinutes(5));

                if (claimed is not null)
                    await _store.RetryAsync(claimed, _now);
            }
        });

        var torn = 0;

        for (var i = 0; i < 100_000 && torn == 0; i++)
        {
            var polled = await _store.ListClaimableAsync(_now, 10);
            torn += polled.Count(IsSelfContradictory);

            var read = await _store.FindAsync(id);
            if (read is not null && IsSelfContradictory(read))
                torn++;
        }

        await stop.CancelAsync();
        await writer;

        torn.Should().Be(0, "a running run carries its lease and a pending one carries none");
    }

    [Fact]
    public async Task GetReturnsNullForAnUnknownIdentity()
    {
        (await _store.FindAsync(404)).Should().BeNull();
    }

    [Fact]
    public async Task ADueRunIsClaimableAndAFutureOneIsNot()
    {
        await _store.EnqueueAsync(Pending("due", _now));
        await _store.EnqueueAsync(Pending("later", _now.AddHours(1)));

        var claimable = await _store.ListClaimableAsync(_now, 10);

        claimable.Select(r => r.JobName).Should().Equal("due");
    }

    [Fact]
    public async Task ClaimableRunsComeBackOldestDueFirst()
    {
        await _store.EnqueueAsync(Pending("second", _now.AddMinutes(-1)));
        await _store.EnqueueAsync(Pending("first", _now.AddMinutes(-5)));

        var claimable = await _store.ListClaimableAsync(_now, 10);

        claimable.Select(r => r.JobName).Should().Equal("first", "second");
    }

    [Fact]
    public async Task TheBatchSizeCapsAPass()
    {
        await _store.EnqueueAsync(Pending("a", _now));
        await _store.EnqueueAsync(Pending("b", _now));

        (await _store.ListClaimableAsync(_now, 1)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ClaimingMarksTheRunRunningAndCountsTheAttempt()
    {
        var run = Pending("report", _now);
        await _store.EnqueueAsync(run);

        var claimed = await _store.ClaimAsync(run, "instance-a", _now, _now.AddMinutes(5));

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(JobRunStatus.Running);
        claimed.AttemptCount.Should().Be(1);
        claimed.LeaseOwner.Should().Be("instance-a");
        claimed.LeaseExpiresAt.Should().Be(_now.AddMinutes(5));
        claimed.ConcurrencyToken.Should().NotBe(run.ConcurrencyToken);
    }

    [Fact]
    public async Task ASecondInstanceCannotClaimTheSameOccurrence()
    {
        // This is the guarantee the whole design rests on: one occurrence, one execution.
        var candidate = await EnqueueAndReadAsync("report", _now);

        var first = await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));
        var second = await _store.ClaimAsync(candidate, "instance-b", _now, _now.AddMinutes(5));

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task AnExpiredLeaseMakesTheRunClaimableAgain()
    {
        // Stands in for an instance that died mid-run: its work must not be lost.
        var candidate = await EnqueueAndReadAsync("report", _now);
        await _store.ClaimAsync(candidate, "crashed", _now, _now.AddMinutes(5));

        var afterExpiry = _now.AddMinutes(6);
        var claimable = await _store.ListClaimableAsync(afterExpiry, 10);

        claimable.Should().HaveCount(1);

        var reclaimed = await _store.ClaimAsync(claimable[0], "healthy", afterExpiry, afterExpiry.AddMinutes(5));

        reclaimed.Should().NotBeNull();
        reclaimed!.LeaseOwner.Should().Be("healthy");
        reclaimed.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ALiveLeaseKeepsTheRunOutOfTheClaimableSet()
    {
        var candidate = await EnqueueAndReadAsync("report", _now);
        await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));

        (await _store.ListClaimableAsync(_now.AddMinutes(1), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimingARunThatWasCancelledInTheMeantimeFails()
    {
        var candidate = await EnqueueAndReadAsync("report", _now);
        await _store.CancelAsync(candidate.Id);

        (await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5))).Should().BeNull();
    }

    [Theory]
    [InlineData(JobRunStatus.Completed)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.Skipped)]
    public async Task SettlingRecordsTheOutcomeAndReleasesTheLease(JobRunStatus status)
    {
        var claimed = await ClaimAsync("report", _now);

        await _store.SettleAsync(claimed, status, _now.AddMinutes(1));

        var stored = await _store.FindAsync(claimed.Id);
        stored!.Status.Should().Be(status);
        stored.SettledAt.Should().Be(_now.AddMinutes(1));
        stored.LeaseOwner.Should().BeNull();
        stored.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task SettlingWithAStaleTokenIsIgnoredBecauseTheLeaseWasLost()
    {
        var claimed = await ClaimAsync("report", _now);
        var stale = Clone(claimed);
        stale.ConcurrencyToken = Guid.NewGuid();

        await _store.SettleAsync(stale, JobRunStatus.Completed, _now.AddMinutes(1));

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Running);
    }

    [Fact]
    public async Task RetryingReturnsTheRunToPendingAtTheNewDueDateAndKeepsTheAttemptCount()
    {
        var claimed = await ClaimAsync("report", _now);

        await _store.RetryAsync(claimed, _now.AddMinutes(10));

        var stored = await _store.FindAsync(claimed.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(10));
        stored.AttemptCount.Should().Be(1);
        stored.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task RetryingARecurringRunGivesUpTheSlotItsSuccessorAlreadyHolds()
    {
        // The Allow case: the successor went out before the work started, so the retry is a leftover
        // attempt at a past occurrence and must not claim to be the job's pending one. The persistent
        // store has a unique index that would reject exactly that write.
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Allow);
        var candidate = (await _store.ListClaimableAsync(_now, 10))[0];
        var claimed = await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Allow);

        await _store.RetryAsync(claimed!, _now.AddMinutes(10));

        var stored = await _store.FindAsync(claimed!.Id);
        stored!.Status.Should().Be(JobRunStatus.Pending);
        stored.DueAt.Should().Be(_now.AddMinutes(10));
        stored.AttemptCount.Should().Be(1, "the retry semantics are unchanged; only the slot is released");
        stored.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task RetryingARecurringRunKeepsTheSlotWhileNoSuccessorExists()
    {
        // The Forbid case: the successor only appears once the run has settled, so the retry genuinely
        // is the job's pending occurrence and has to go on blocking a duplicate.
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Forbid);
        var candidate = (await _store.ListClaimableAsync(_now, 10))[0];
        var claimed = await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));

        await _store.RetryAsync(claimed!, _now.AddMinutes(10));

        (await _store.FindAsync(claimed!.Id))!.IsRecurring.Should().BeTrue();
        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Forbid))
            .Should().BeFalse();
    }

    [Fact]
    public async Task RetryingWithAStaleTokenIsIgnored()
    {
        var claimed = await ClaimAsync("report", _now);
        var stale = Clone(claimed);
        stale.ConcurrencyToken = Guid.NewGuid();

        await _store.RetryAsync(stale, _now.AddMinutes(10));

        (await _store.FindAsync(claimed.Id))!.Status.Should().Be(JobRunStatus.Running);
    }

    [Fact]
    public async Task CancellingRemovesAPendingRun()
    {
        var id = await _store.EnqueueAsync(Pending("report", _now));

        (await _store.CancelAsync(id)).Should().BeTrue();
        (await _store.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task CancellingARunningRunIsRefusedSoItsOwnerIsNotStranded()
    {
        var claimed = await ClaimAsync("report", _now);

        (await _store.CancelAsync(claimed.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task CancellingAnUnknownRunReportsFailure()
    {
        (await _store.CancelAsync(404)).Should().BeFalse();
    }

    [Fact]
    public async Task ARecurringJobGetsOnlyOneLiveOccurrence()
    {
        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Forbid)).Should().BeTrue();
        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Forbid)).Should().BeFalse();
    }

    [Fact]
    public async Task ARunningOccurrenceStillOccupiesTheSlot()
    {
        // Otherwise the next occurrence could be created while the current one is executing, which is
        // exactly the overlap the Forbid policy promises not to allow.
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Forbid);
        var candidate = (await _store.ListClaimableAsync(_now, 10))[0];
        await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));

        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Forbid)).Should().BeFalse();
    }

    [Fact]
    public async Task TheSlotIsFreedOnceTheOccurrenceHasSettled()
    {
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Forbid);
        var candidate = (await _store.ListClaimableAsync(_now, 10))[0];
        var claimed = await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));
        await _store.SettleAsync(claimed!, JobRunStatus.Completed, _now.AddMinutes(1));

        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Forbid)).Should().BeTrue();
    }

    [Fact]
    public async Task UnderAllowARunningOccurrenceDoesNotBlockItsSuccessor()
    {
        // Keeping the cadence across a slow run is the entire purpose of the Allow policy.
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Allow);
        var candidate = (await _store.ListClaimableAsync(_now, 10))[0];
        await _store.ClaimAsync(candidate, "instance-a", _now, _now.AddMinutes(5));

        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Allow))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EvenUnderAllowASecondPendingOccurrenceIsRefused()
    {
        await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Allow);

        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now.AddDays(1)), JobOverlapPolicy.Allow))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AOneShotRunUnderTheSameNameDoesNotOccupyTheRecurringSlot()
    {
        // A one-shot run enqueued under a recurring job's name is ordinary work. Letting it hold the
        // slot would stop that job's schedule dead, and the persistent store draws the same line.
        await _store.EnqueueAsync(Pending("nightly", _now));

        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Forbid))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ADifferentRecurringJobHasItsOwnSlot()
    {
        (await _store.TryEnqueueRecurringAsync(Recurring("nightly", _now), JobOverlapPolicy.Forbid)).Should().BeTrue();
        (await _store.TryEnqueueRecurringAsync(Recurring("hourly", _now), JobOverlapPolicy.Forbid)).Should().BeTrue();
    }

    [Fact]
    public async Task ASettledRunIsNoLongerClaimable()
    {
        var claimed = await ClaimAsync("report", _now);
        await _store.SettleAsync(claimed, JobRunStatus.Completed, _now);

        (await _store.ListClaimableAsync(_now.AddHours(1), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task EveryFieldSurvivesARoundTrip()
    {
        var run = new JobRun
        {
            JobName = "report",
            DueAt = _now,
            Status = JobRunStatus.Pending,
            IsRecurring = true,
            Payload = "{\"id\":7}",
            CorrelationId = "corr-1",
            AttemptCount = 2,
            LeaseOwner = "owner",
            LeaseExpiresAt = _now.AddMinutes(5),
            SettledAt = _now.AddMinutes(6),
        };

        var id = await _store.EnqueueAsync(run);
        var stored = await _store.FindAsync(id);

        stored.Should().BeEquivalentTo(run, o => o.Excluding(r => r.ConcurrencyToken));
    }

    [Fact]
    public async Task TheNullGuardsRejectMissingArguments()
    {
        await Assert("run", async () => await _store.EnqueueAsync(null!));
        await Assert("run", async () => await _store.TryEnqueueRecurringAsync(null!, JobOverlapPolicy.Forbid));
        await Assert("run", async () => await _store.ClaimAsync(null!, "o", _now, _now));
        await Assert("owner", async () => await _store.ClaimAsync(Pending("a", _now), null!, _now, _now));
        await Assert("run", async () => await _store.SettleAsync(null!, JobRunStatus.Completed, _now));
        await Assert("run", async () => await _store.RetryAsync(null!, _now));
    }

    private static async Task Assert(string parameterName, Func<Task> act) =>
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName(parameterName);

    /// <summary>
    /// A run whose status and lease contradict each other: claiming sets both, settling and retrying
    /// clear both, so no completed operation can leave one without the other.
    /// </summary>
    private static bool IsSelfContradictory(JobRun run) =>
        run.Status == JobRunStatus.Running
            ? run.LeaseOwner is null || run.LeaseExpiresAt is null
            : run.LeaseOwner is not null || run.LeaseExpiresAt is not null;

    private async Task<JobRun> EnqueueAndReadAsync(string jobName, DateTimeOffset dueAt)
    {
        var id = await _store.EnqueueAsync(Pending(jobName, dueAt));
        return (await _store.FindAsync(id))!;
    }

    private async Task<JobRun> ClaimAsync(string jobName, DateTimeOffset dueAt)
    {
        var candidate = await EnqueueAndReadAsync(jobName, dueAt);
        return (await _store.ClaimAsync(candidate, "instance-a", dueAt, dueAt.AddMinutes(5)))!;
    }

    private static JobRun Pending(string jobName, DateTimeOffset dueAt) => new()
    {
        JobName = jobName,
        DueAt = dueAt,
        Status = JobRunStatus.Pending,
    };

    private static JobRun Recurring(string jobName, DateTimeOffset dueAt) => new()
    {
        JobName = jobName,
        DueAt = dueAt,
        Status = JobRunStatus.Pending,
        IsRecurring = true,
    };

    private static JobRun Clone(JobRun run) => new()
    {
        Id = run.Id,
        JobName = run.JobName,
        DueAt = run.DueAt,
        Status = run.Status,
        IsRecurring = run.IsRecurring,
        Payload = run.Payload,
        CorrelationId = run.CorrelationId,
        AttemptCount = run.AttemptCount,
        LeaseOwner = run.LeaseOwner,
        LeaseExpiresAt = run.LeaseExpiresAt,
        SettledAt = run.SettledAt,
        ConcurrencyToken = run.ConcurrencyToken,
    };
}
