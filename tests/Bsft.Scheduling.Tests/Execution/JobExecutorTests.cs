using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Scheduling.Tests.Execution;

public class JobExecutorTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly JobLog _log = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly BsftSchedulingOptions _options = new();
    private readonly IJobTelemetry _telemetry = Substitute.For<IJobTelemetry>();
    private readonly ServiceProvider _provider;

    public JobExecutorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_log);
        services.AddScoped<RecordingJob>();
        services.AddScoped<FailingJob>();
        services.AddScoped<CorrelationCapturingJob>();
        services.AddScoped<TokenCapturingJob>();
        services.AddScoped<BlockingJob>();
        services.AddScoped<ResultRecordingJob>();
        _provider = services.BuildServiceProvider();

        _telemetry.BeginExecution(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>())
            .Returns(Substitute.For<IJobExecutionTelemetryScope>());
    }

    private JobExecutor CreateExecutor(TimeProvider? time = null) =>
        new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            time ?? _time,
            Options.Create(_options),
            _telemetry,
            new JobRunResultRecorderFactory(Options.Create(_options)));

    private static JobRun Run(string? correlationId = null, string? payload = null, DateTimeOffset? leaseExpiresAt = null) => new()
    {
        Id = 7,
        JobName = "report",
        DueAt = _now.AddMinutes(-1),
        Status = JobRunStatus.Running,
        AttemptCount = 2,
        CorrelationId = correlationId,
        Payload = payload,
        LeaseExpiresAt = leaseExpiresAt,
    };

    [Fact]
    public async Task TheJobIsResolvedFromItsOwnScopeAndRun()
    {
        await CreateExecutor().ExecuteAsync(typeof(RecordingJob), Run());

        _log.Count.Should().Be(1);
    }

    [Fact]
    public async Task TheContextDescribesTheRunTheJobIsExecutingFor()
    {
        await CreateExecutor().ExecuteAsync(typeof(RecordingJob), Run("corr-1", "payload"));

        var context = _log.Executions[0];
        context.JobId.Should().Be(7);
        context.JobName.Should().Be("report");
        context.ScheduledFor.Should().Be(_now.AddMinutes(-1));
        context.StartedAt.Should().Be(_now);
        context.AttemptNumber.Should().Be(2);
        context.CorrelationId.Should().Be("corr-1");
        context.Payload.Should().Be("payload");
    }

    [Fact]
    public async Task AResultTheJobRecordsLandsOnTheRunWithinTheConfiguredBound()
    {
        _options.MaxResultBytes = 64;
        var run = Run();

        await CreateExecutor().ExecuteAsync(typeof(ResultRecordingJob), run);

        run.Result.Should().Be(TestRunResults.Of("report/v1", """{"rows":3}"""));
    }

    [Fact]
    public async Task AJobThatRecordsNothingLeavesTheRunWithoutAResult()
    {
        var run = Run();

        await CreateExecutor().ExecuteAsync(typeof(RecordingJob), run);

        run.Result.Should().BeNull();
    }

    [Fact]
    public async Task TheCorrelationIdIsAmbientForEverythingResolvedInsideTheRun()
    {
        await CreateExecutor().ExecuteAsync(typeof(CorrelationCapturingJob), Run("corr-77"));

        CorrelationCapturingJob.LastSeen.Should().Be("corr-77");
    }

    [Fact]
    public async Task TheAmbientCorrelationIdIsRestoredAfterTheRun()
    {
        await CreateExecutor().ExecuteAsync(typeof(RecordingJob), Run("corr-1"));

        JobCorrelationScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task ASuccessfulRunIsReportedToTelemetry()
    {
        var scope = Substitute.For<IJobExecutionTelemetryScope>();
        _telemetry.BeginExecution("report", 7, 2).Returns(scope);

        await CreateExecutor().ExecuteAsync(typeof(RecordingJob), Run());

        scope.Received(1).Succeeded();
        scope.Received(1).Dispose();
    }

    [Fact]
    public async Task AFailingJobLetsTheExceptionOutAndIsNotReportedAsSucceeded()
    {
        var scope = Substitute.For<IJobExecutionTelemetryScope>();
        _telemetry.BeginExecution("report", 7, 2).Returns(scope);

        var act = async () => await CreateExecutor().ExecuteAsync(typeof(FailingJob), Run());

        await act.Should().ThrowAsync<InvalidOperationException>();
        scope.DidNotReceive().Succeeded();
        scope.Received(1).Dispose();
    }

    [Fact]
    public async Task AnExecutionIsBoundedByWhatIsLeftOfTheLeaseNotByAFreshLeaseDuration()
    {
        // The lease started at claim time and the dispatcher spent part of it before getting here, so
        // a fresh LeaseDuration would let the job run past the instant every other instance is allowed
        // to reclaim the run — and execute it a second time in parallel.
        _options.LeaseDuration = TimeSpan.FromSeconds(30);
        var remaining = TimeSpan.FromMilliseconds(200);
        var clock = new ManualTimeProvider(_now);
        var running = CreateExecutor(clock).ExecuteAsync(typeof(BlockingJob), Run(leaseExpiresAt: _now + remaining));

        await clock.WaitForTimerAsync(remaining);
        running.IsCompleted.Should().BeFalse("the job is still inside what is left of the lease");
        clock.Advance(remaining);

        await running.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AJobWhoseLeaseHasAlreadyLapsedSeesACancelledTokenAtOnce()
    {
        // Whoever reclaims the run owns it now; this instance must not keep working on it.
        await CreateExecutor().ExecuteAsync(typeof(TokenCapturingJob), Run(leaseExpiresAt: _now.AddSeconds(-1)));

        TokenCapturingJob.LastSeenCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ARunWithoutALeaseFallsBackToTheConfiguredLeaseDuration()
    {
        // Nothing claimed this run, so there is no lease to stay inside and the configured duration is
        // the only bound there is.
        _options.LeaseDuration = TimeSpan.FromMilliseconds(50);
        var clock = new ManualTimeProvider(_now);
        var running = CreateExecutor(clock).ExecuteAsync(typeof(BlockingJob), Run());

        await clock.WaitForTimerAsync(_options.LeaseDuration);
        clock.Advance(_options.LeaseDuration);

        await running.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TheNullGuardsRejectMissingArguments()
    {
        var executor = CreateExecutor();

        await executor.Invoking(e => e.ExecuteAsync(null!, Run()))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobType");
        await executor.Invoking(e => e.ExecuteAsync(typeof(RecordingJob), null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("run");
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(_options);
        var recorders = new JobRunResultRecorderFactory(options);

        ((Action)(() => new JobExecutor(null!, _time, options, _telemetry, recorders)))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => new JobExecutor(scopeFactory, null!, options, _telemetry, recorders)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new JobExecutor(scopeFactory, _time, null!, _telemetry, recorders)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new JobExecutor(scopeFactory, _time, options, null!, recorders)))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        ((Action)(() => new JobExecutor(scopeFactory, _time, options, _telemetry, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("resultRecorders");
    }

    /// <summary>Records whether the token the job was handed was already cancelled when it started.</summary>
    private sealed class ResultRecordingJob : IJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.ResultRecorder.Record("report/v1", new { Rows = 3 });
            return Task.CompletedTask;
        }
    }

    private sealed class TokenCapturingJob : IJob
    {
        public static bool LastSeenCancelled { get; private set; }

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
        {
            LastSeenCancelled = ct.IsCancellationRequested;
            return Task.CompletedTask;
        }
    }

    /// <summary>A job that never finishes on its own, so only the lease boundary can end it.</summary>
    private sealed class BlockingJob : IJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default) =>
            Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    /// <summary>Reads the ambient correlation id the way a scoped component inside the run would.</summary>
    private sealed class CorrelationCapturingJob : IJob
    {
        public static string? LastSeen { get; private set; }

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
        {
            LastSeen = new AmbientJobCorrelationAccessor().CorrelationId;
            return Task.CompletedTask;
        }
    }
}
