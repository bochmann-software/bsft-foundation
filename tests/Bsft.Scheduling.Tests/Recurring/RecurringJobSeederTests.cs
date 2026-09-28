using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Scheduling.Tests.Recurring;

public class RecurringJobSeederTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _time = new(_now);
    private readonly BsftSchedulingOptions _options = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(1),
        RecurringSeedInterval = TimeSpan.FromMilliseconds(1),
    };

    private RecurringJobSeeder CreateSeeder(IRecurringJobPlanner planner) =>
        new(planner, _time, Options.Create(_options), NullLogger<RecurringJobSeeder>.Instance);

    [Fact]
    public async Task SeedingRepeatsAfterASuccessSoABrokenRecurrenceChainIsRepaired()
    {
        // The schedule sustains itself only as long as every settled occurrence manages to write its
        // successor. A crash or a store failure in that gap leaves the job with no pending occurrence,
        // and seeding only at startup would leave it silently dead until some instance restarted.
        using var cts = new CancellationTokenSource();
        var planner = new StubPlanner { CancelAfter = 3, Cts = cts };

        await CreateSeeder(planner).SeedPeriodicallyAsync(cts.Token);

        planner.Attempts.Should().Be(3, "seeding is idempotent, so it can simply be repeated");
    }

    [Fact]
    public async Task SeedingIsRetriedUntilItSucceeds()
    {
        // A store that is not reachable yet is ordinary during a rolling deployment. Without the
        // retry the recurring jobs would stay silent for the life of the process.
        using var cts = new CancellationTokenSource();
        var planner = new StubPlanner { FailuresBeforeSuccess = 2, CancelAfter = 3, Cts = cts };

        await CreateSeeder(planner).SeedPeriodicallyAsync(cts.Token);

        planner.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task SeedingStopsWhenTheHostShutsDownBeforeItSucceeded()
    {
        using var cts = new CancellationTokenSource();
        var planner = new StubPlanner { FailuresBeforeSuccess = int.MaxValue, CancelAfter = 2, Cts = cts };

        await CreateSeeder(planner).Invoking(s => s.SeedPeriodicallyAsync(cts.Token)).Should().NotThrowAsync();

        planner.Attempts.Should().BeLessThan(int.MaxValue);
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenSeedsNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var planner = new StubPlanner();

        await CreateSeeder(planner).SeedPeriodicallyAsync(cts.Token);

        planner.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task ASeedCancelledByTheShutdownTokenEndsTheRetryLoop()
    {
        // The planner observes the stopping token and reports cancellation rather than throwing,
        // which has to unwind the loop instead of being logged and retried forever.
        var seeder = CreateSeeder(new CancelingPlanner());

        await seeder.Invoking(s => s.SeedPeriodicallyAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TheHostedServiceSeedsAndStopsCleanly()
    {
        // Exercises the BackgroundService entry point rather than the loop directly. The seed interval
        // outlasts the test, so the service is stopped while waiting out its first one.
        _options.RecurringSeedInterval = TimeSpan.FromMinutes(5);
        var planner = new StubPlanner();
        var seeder = CreateSeeder(planner);

        await seeder.StartAsync(CancellationToken.None);
        // Since Microsoft.Extensions.Hosting 10, StartAsync runs the whole of ExecuteAsync on the
        // thread pool, so a stop right after the start can come before the first attempt. The test
        // waits for it rather than relying on the synchronous start of earlier versions.
        await planner.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(10));
        await seeder.StopAsync(CancellationToken.None);

        planner.Attempts.Should().Be(1);
        seeder.Dispose();
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var planner = new StubPlanner();
        var logger = NullLogger<RecurringJobSeeder>.Instance;

        ((Action)(() => new RecurringJobSeeder(null!, _time, Options.Create(_options), logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("planner");
        ((Action)(() => new RecurringJobSeeder(planner, null!, Options.Create(_options), logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new RecurringJobSeeder(planner, _time, null!, logger)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new RecurringJobSeeder(planner, _time, Options.Create(_options), null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>Reports cancellation from a seeding attempt, standing in for a planner observing shutdown.</summary>
    private sealed class CancelingPlanner : IRecurringJobPlanner
    {
        public Task<bool> TryScheduleNextAsync(string jobName, DateTimeOffset previousDueAt, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task SeedAsync(CancellationToken ct = default) =>
            Task.FromCanceled(new CancellationToken(canceled: true));
    }

    private sealed class StubPlanner : IRecurringJobPlanner
    {
        private readonly TaskCompletionSource _firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Attempts { get; private set; }

        public Task FirstAttempt => _firstAttempt.Task;

        public int FailuresBeforeSuccess { get; init; }

        public int CancelAfter { get; init; } = int.MaxValue;

        public CancellationTokenSource? Cts { get; init; }

        public Task<bool> TryScheduleNextAsync(string jobName, DateTimeOffset previousDueAt, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task SeedAsync(CancellationToken ct = default)
        {
            Attempts++;
            _firstAttempt.TrySetResult();

            if (Attempts >= CancelAfter)
                Cts?.Cancel();

            if (Attempts <= FailuresBeforeSuccess)
                throw new InvalidOperationException("the store is unreachable");

            return Task.CompletedTask;
        }
    }
}
