using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
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
        _provider = services.BuildServiceProvider();

        _telemetry.BeginExecution(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>())
            .Returns(Substitute.For<IJobExecutionTelemetryScope>());
    }

    private JobExecutor CreateExecutor() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), _time, Options.Create(_options), _telemetry);

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
        var run = Run(leaseExpiresAt: _now.AddMilliseconds(200));

        var act = async () => await CreateExecutor()
            .ExecuteAsync(typeof(BlockingJob), run)
            .WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<OperationCanceledException>();
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

        var act = async () => await CreateExecutor()
            .ExecuteAsync(typeof(BlockingJob), Run())
            .WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<OperationCanceledException>();
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

        ((Action)(() => new JobExecutor(null!, _time, Options.Create(_options), _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
        ((Action)(() => new JobExecutor(scopeFactory, null!, Options.Create(_options), _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new JobExecutor(scopeFactory, _time, null!, _telemetry)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new JobExecutor(scopeFactory, _time, Options.Create(_options), null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    /// <summary>Records whether the token the job was handed was already cancelled when it started.</summary>
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
