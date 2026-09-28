using Bsft.Core.Polling;
using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests.Execution;

public class JobDispatcherTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryJobStore _store = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly RecordingRunner _runner = new();
    private readonly BsftSchedulingOptions _options = new() { PollInterval = TimeSpan.FromMilliseconds(1) };
    private readonly IJobTelemetry _telemetry = Substitute.For<IJobTelemetry>();

    private JobDispatcher CreateDispatcher() =>
        new(_store, _runner, _time, Options.Create(_options), _telemetry, NullLogger<JobDispatcher>.Instance);

    private JobDispatcher CreateDispatcher(IJobStore store) =>
        new(store, _runner, _time, Options.Create(_options), _telemetry, NullLogger<JobDispatcher>.Instance);

    private Task<long> EnqueueAsync(string jobName, DateTimeOffset dueAt) =>
        _store.EnqueueAsync(new JobRun { JobName = jobName, DueAt = dueAt, Status = JobRunStatus.Pending });

    [Fact]
    public async Task ADueRunIsClaimedAndHandedToTheRunner()
    {
        await EnqueueAsync("report", _now);

        var claimed = await CreateDispatcher().ProcessOnceAsync();

        claimed.Should().Be(1);
        _runner.Runs.Should().ContainSingle();
        _runner.Runs[0].Status.Should().Be(JobRunStatus.Running);
    }

    [Fact]
    public async Task ARunThatIsNotDueYetIsLeftAlone()
    {
        await EnqueueAsync("report", _now.AddHours(1));

        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(0);
        _runner.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyStoreIsAnEmptyPass()
    {
        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheLeaseOwnerIsTheConfiguredInstanceIdentity()
    {
        _options.InstanceId = "worker-7";
        await EnqueueAsync("report", _now);

        await CreateDispatcher().ProcessOnceAsync();

        _runner.Runs[0].LeaseOwner.Should().Be("worker-7");
        _runner.Runs[0].LeaseExpiresAt.Should().Be(_now + _options.LeaseDuration);
    }

    [Fact]
    public async Task EachClaimTakesItsLeaseFromTheInstantItWasClaimed()
    {
        // The batch is claimed and executed one run at a time, so by the time a later candidate is
        // claimed the instant the pass started is long gone. A lease derived from that instant is
        // already partly spent when the work begins, and a slow enough predecessor leaves it lapsing
        // mid-execution — another instance then reclaims the run and executes it in parallel.
        _runner.AdvanceAfterEachRun(TimeSpan.FromMinutes(2), _time);
        await EnqueueAsync("a", _now);
        await EnqueueAsync("b", _now);

        await CreateDispatcher().ProcessOnceAsync();

        _runner.Runs[0].LeaseExpiresAt.Should().Be(_now + _options.LeaseDuration);
        _runner.Runs[1].LeaseExpiresAt.Should().Be(_now.AddMinutes(2) + _options.LeaseDuration);
    }

    [Fact]
    public async Task AClaimIsCounted()
    {
        await EnqueueAsync("report", _now);

        await CreateDispatcher().ProcessOnceAsync();

        _telemetry.Received(1).JobClaimed("report");
    }

    [Fact]
    public async Task LosingTheClaimRaceIsNotAnErrorAndTheRunIsSimplySkipped()
    {
        // The normal outcome when several instances poll the same store: the winner is running it.
        await EnqueueAsync("report", _now);
        var loser = CreateDispatcher(new StealingJobStore(_store));

        (await loser.ProcessOnceAsync()).Should().Be(0);
        _runner.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task TheBatchSizeCapsHowMuchOnePassTakes()
    {
        _options.BatchSize = 1;
        await EnqueueAsync("a", _now);
        await EnqueueAsync("b", _now);

        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TheLoopStopsWhenTheHostShutsDown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await CreateDispatcher().Invoking(d => d.RunLoopAsync(cts.Token)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TheLoopDrainsEveryDueRunAndThenStops()
    {
        await EnqueueAsync("a", _now);
        await EnqueueAsync("b", _now);

        using var cts = new CancellationTokenSource();
        _runner.CancelAfter(2, cts);

        await CreateDispatcher().RunLoopAsync(cts.Token);

        _runner.Runs.Should().HaveCount(2);
    }

    [Fact]
    public async Task AStoreThatIsBrieflyUnreachableDoesNotKillTheScheduler()
    {
        // A transient failure must cost one pass, not the lifetime of the process.
        var store = new FailingJobStore();
        using var cts = new CancellationTokenSource();
        store.CancelAfter(2, cts);

        await CreateDispatcher(store).Invoking(d => d.RunLoopAsync(cts.Token)).Should().NotThrowAsync();

        store.Attempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task AFailingPassIsReportedAsClaimingNothing()
    {
        (await CreateDispatcher(new FailingJobStore()).Invoking(d => d.ProcessOnceAsync())
            .Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("store");
    }

    [Fact]
    public async Task APassCancelledByTheShutdownEndsTheLoopCleanly()
    {
        // The store observes the stopping token and reports cancellation rather than throwing, which
        // has to unwind the loop once the host is actually stopping.
        var store = new CancelingJobStore();
        using var cts = new CancellationTokenSource();
        store.CancelAfter(1, cts);

        await CreateDispatcher(store).Invoking(d => d.RunLoopAsync(cts.Token)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task APassCancelledWithoutAStopRequestKeepsPollingAndReportsAFailedPass()
    {
        // A command timeout inside the store cancels the pass while the host is perfectly healthy.
        // Reading that as a shutdown would stop dispatching jobs for the lifetime of the process.
        var store = new CancelingJobStore();
        using var cts = new CancellationTokenSource();
        store.CancelAfter(3, cts);
        var logger = new RecordingLogger<JobDispatcher>();
        var dispatcher = new JobDispatcher(store, _runner, _time, Options.Create(_options), _telemetry, logger);

        await dispatcher.Invoking(d => d.RunLoopAsync(cts.Token)).Should().NotThrowAsync();

        store.Attempts.Should().BeGreaterThanOrEqualTo(3);
        logger.Levels.Count(level => level == LogLevel.Error).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void TheLoopSaysFailuresLoudlyAndTheStopQuietly()
    {
        var logger = new RecordingLogger<JobDispatcher>();
        IPollReporter reporter = new JobDispatcher(
            _store, _runner, _time, Options.Create(_options), _telemetry, logger);

        reporter.ReportPassFailed(new InvalidOperationException("the store is unreachable"));
        reporter.ReportPassCanceledWithoutStop();
        reporter.ReportStopping();

        logger.Levels.Count(level => level == LogLevel.Error).Should().Be(2);
        logger.Levels.Should().Contain(LogLevel.Debug);
    }

    [Fact]
    public async Task TheHostedServiceRunsTheLoopAndStopsCleanly()
    {
        // Exercises the BackgroundService entry point rather than the loop directly.
        _options.PollInterval = TimeSpan.FromMinutes(5);
        await EnqueueAsync("report", _now);
        var dispatcher = CreateDispatcher();

        await dispatcher.StartAsync(CancellationToken.None);
        // Since Microsoft.Extensions.Hosting 10, StartAsync runs the whole of ExecuteAsync on the
        // thread pool, so a stop right after the start can come before the first pass. The test waits
        // for the due run rather than relying on the synchronous start of earlier versions.
        SpinWait.SpinUntil(() => _runner.Runs.Count == 1, TimeSpan.FromSeconds(10)).Should().BeTrue();
        await dispatcher.StopAsync(CancellationToken.None);

        dispatcher.Dispose();
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var logger = NullLogger<JobDispatcher>.Instance;

        ((Action)(() => new JobDispatcher(null!, _runner, _time, Options.Create(_options), _telemetry, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        ((Action)(() => new JobDispatcher(_store, null!, _time, Options.Create(_options), _telemetry, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("runner");
        ((Action)(() => new JobDispatcher(_store, _runner, null!, Options.Create(_options), _telemetry, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new JobDispatcher(_store, _runner, _time, null!, _telemetry, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new JobDispatcher(_store, _runner, _time, Options.Create(_options), null!, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        ((Action)(() => new JobDispatcher(_store, _runner, _time, Options.Create(_options), _telemetry, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>Records what the dispatcher handed it and can stop the loop after a given count.</summary>
    private sealed class RecordingRunner : IJobRunner
    {
        private readonly List<JobRun> _runs = [];
        private int _cancelAfter = int.MaxValue;
        private CancellationTokenSource? _cts;
        private TimeSpan _advanceBy = TimeSpan.Zero;
        private FixedTimeProvider? _time;

        public IReadOnlyList<JobRun> Runs => _runs;

        public void CancelAfter(int count, CancellationTokenSource cts)
        {
            _cancelAfter = count;
            _cts = cts;
        }

        /// <summary>Moves the clock on once a run has been handed over, standing in for a slow job.</summary>
        public void AdvanceAfterEachRun(TimeSpan by, FixedTimeProvider time)
        {
            _advanceBy = by;
            _time = time;
        }

        public Task RunAsync(JobRun claimed, CancellationToken ct = default)
        {
            _runs.Add(claimed);

            if (_runs.Count >= _cancelAfter)
                _cts?.Cancel();

            _time?.Advance(_advanceBy);

            return Task.CompletedTask;
        }
    }

    /// <summary>Claims every candidate itself before the dispatcher gets a chance, standing in for a rival instance.</summary>
    private sealed class StealingJobStore : IJobStore
    {
        private readonly InMemoryJobStore _inner;

        public StealingJobStore(InMemoryJobStore inner) => _inner = inner;

        public Task<long> EnqueueAsync(JobRun run, CancellationToken ct = default) => _inner.EnqueueAsync(run, ct);

        public Task<bool> TryEnqueueRecurringAsync(JobRun run, JobOverlapPolicy overlapPolicy, CancellationToken ct = default) =>
            _inner.TryEnqueueRecurringAsync(run, overlapPolicy, ct);

        public async Task<IReadOnlyList<JobRun>> ListClaimableAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            var claimable = await _inner.ListClaimableAsync(now, batchSize, ct);

            foreach (var run in claimable)
            {
                await _inner.ClaimAsync(run, "rival", now, now.AddMinutes(5), ct);
            }

            return claimable;
        }

        public Task<JobRun?> ClaimAsync(JobRun run, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken ct = default) =>
            _inner.ClaimAsync(run, owner, now, leaseExpiresAt, ct);

        public Task SettleAsync(JobRun run, JobRunStatus status, DateTimeOffset settledAt, CancellationToken ct = default) =>
            _inner.SettleAsync(run, status, settledAt, ct);

        public Task RetryAsync(JobRun run, DateTimeOffset dueAt, CancellationToken ct = default) =>
            _inner.RetryAsync(run, dueAt, ct);

        public Task<bool> CancelAsync(long id, CancellationToken ct = default) => _inner.CancelAsync(id, ct);

        public Task<JobRun?> FindAsync(long id, CancellationToken ct = default) => _inner.FindAsync(id, ct);
    }

    /// <summary>
    /// Reports cancellation from every poll, standing in for a store that observes a token of its own —
    /// a command timeout — or the shutdown token. Which of the two it is, is decided by whether the
    /// test has cancelled the stopping token, exactly as the loop decides it.
    /// </summary>
    private sealed class CancelingJobStore : IJobStore
    {
        private int _cancelAfter = int.MaxValue;
        private CancellationTokenSource? _cts;

        public int Attempts { get; private set; }

        public void CancelAfter(int count, CancellationTokenSource cts)
        {
            _cancelAfter = count;
            _cts = cts;
        }

        public Task<IReadOnlyList<JobRun>> ListClaimableAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            Attempts++;

            if (Attempts >= _cancelAfter)
                _cts?.Cancel();

            return Task.FromCanceled<IReadOnlyList<JobRun>>(new CancellationToken(canceled: true));
        }

        public Task<long> EnqueueAsync(JobRun run, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> TryEnqueueRecurringAsync(JobRun run, JobOverlapPolicy overlapPolicy, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<JobRun?> ClaimAsync(JobRun run, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SettleAsync(JobRun run, JobRunStatus status, DateTimeOffset settledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RetryAsync(JobRun run, DateTimeOffset dueAt, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> CancelAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<JobRun?> FindAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Fails every poll, standing in for a database that is briefly unreachable.</summary>
    private sealed class FailingJobStore : IJobStore
    {
        private int _cancelAfter = int.MaxValue;
        private CancellationTokenSource? _cts;

        public int Attempts { get; private set; }

        public void CancelAfter(int count, CancellationTokenSource cts)
        {
            _cancelAfter = count;
            _cts = cts;
        }

        public Task<IReadOnlyList<JobRun>> ListClaimableAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            Attempts++;

            if (Attempts >= _cancelAfter)
                _cts?.Cancel();

            throw new InvalidOperationException("the store is unreachable");
        }

        public Task<long> EnqueueAsync(JobRun run, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> TryEnqueueRecurringAsync(JobRun run, JobOverlapPolicy overlapPolicy, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<JobRun?> ClaimAsync(JobRun run, string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SettleAsync(JobRun run, JobRunStatus status, DateTimeOffset settledAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RetryAsync(JobRun run, DateTimeOffset dueAt, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> CancelAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<JobRun?> FindAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
