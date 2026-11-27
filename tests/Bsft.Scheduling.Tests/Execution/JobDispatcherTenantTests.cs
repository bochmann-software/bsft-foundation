using Bsft.Core.Tenancy;
using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Tests.Shared;
using NSubstitute;

namespace Bsft.Scheduling.Tests.Execution;

public class JobDispatcherTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private JobDispatcher CreateDispatcher(
        IJobStore? store = null,
        IJobRunner? runner = null,
        IBackgroundTenantSource? tenants = null,
        TimeProvider? clock = null)
    {
        var jobStore = store ?? Substitute.For<IJobStore>();
        var jobRunner = runner ?? Substitute.For<IJobRunner>();
        var options = Options.Create(new BsftSchedulingOptions());
        var telemetry = Substitute.For<IJobTelemetry>();
        var timeProvider = clock ?? new FixedTimeProvider(Now);

        if (tenants is null)
        {
            return new JobDispatcher(
                jobStore,
                jobRunner,
                timeProvider,
                options,
                telemetry,
                NullLogger<JobDispatcher>.Instance);
        }

        return new JobDispatcher(
            jobStore,
            jobRunner,
            timeProvider,
            options,
            telemetry,
            NullLogger<JobDispatcher>.Instance,
            tenants);
    }

    [Fact]
    public async Task ProcessOnceAsync_WithATenantSource_ReadsTheScheduleOncePerTenant()
    {
        var (store, listedFor) = RecordingStore();
        var clock = new ManualTimeProvider(Now);
        var dispatcher = CreateDispatcher(store, tenants: new FakeBackgroundTenantSource(["a", "b"]), clock: clock);

        await RunFirstPassAsync(dispatcher, clock);

        listedFor.Take(2).Should().Equal("a", "b");
    }

    [Fact]
    public async Task ProcessOnceAsync_WithoutATenantSource_ReadsTheScheduleWithoutATenant()
    {
        var (store, listedFor) = RecordingStore();
        var clock = new ManualTimeProvider(Now);
        var dispatcher = CreateDispatcher(store, clock: clock);

        await RunFirstPassAsync(dispatcher, clock);

        listedFor[0].Should().BeNull();
    }

    private static (IJobStore Store, List<string?> ListedFor) RecordingStore()
    {
        var listedFor = new List<string?>();
        var store = Substitute.For<IJobStore>();
        store.ListClaimableAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                lock (listedFor)
                    listedFor.Add(BackgroundTenant.Current);
                return Task.FromResult<IReadOnlyList<JobRun>>([]);
            });

        return (store, listedFor);
    }

    private static async Task RunFirstPassAsync(JobDispatcher dispatcher, ManualTimeProvider clock)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = dispatcher.RunLoopAsync(cts.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(clock.WaitForAnyTimerAsync(), loop);

        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public void Constructor_WithNullTenants_Throws()
    {
        var store = Substitute.For<IJobStore>();
        var runner = Substitute.For<IJobRunner>();
        var options = Options.Create(new BsftSchedulingOptions());
        var telemetry = Substitute.For<IJobTelemetry>();
        var timeProvider = new FixedTimeProvider(Now);

        var act = () => new JobDispatcher(
            store,
            runner,
            timeProvider,
            options,
            telemetry,
            NullLogger<JobDispatcher>.Instance,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenants");
    }
}
