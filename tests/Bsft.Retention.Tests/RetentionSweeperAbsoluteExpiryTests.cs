using Bsft.Core.Polling;
using Bsft.Retention.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Bsft.Retention.Tests;

public sealed class RetentionSweeperAbsoluteExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private static (RetentionSweeper<ExpiringRecordsRetentionPass> Sweeper, ExpiringRecordsRetentionPass Pass, FakeTimeProvider Clock)
        Build(Action<BsftRetentionOptions>? configure = null)
    {
        var pass = new ExpiringRecordsRetentionPass();
        var clock = new FakeTimeProvider(Now);

        var options = new BsftRetentionOptions { UseAbsoluteExpiry = true, MaxStartupStagger = TimeSpan.Zero };
        configure?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<BsftRetentionOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(options);

        var services = new ServiceCollection();
        services.AddScoped(_ => pass);
        var provider = services.BuildServiceProvider();

        var sweeper = new RetentionSweeper<ExpiringRecordsRetentionPass>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            clock,
            NullLogger<RetentionSweeper<ExpiringRecordsRetentionPass>>.Instance);

        return (sweeper, pass, clock);
    }

    [Fact]
    public async Task SweepAsync_RecordExpiringExactlyNow_IsPurged()
    {
        var (sweeper, pass, _) = Build();
        pass.ExpiresAt.Add(Now);

        await sweeper.SweepAsync(null);

        pass.ExpiresAt.Should().BeEmpty();
        pass.Cutoffs.Should().Equal(Now);
    }

    [Fact]
    public async Task SweepAsync_RecordExpiringOneTickLater_IsKept()
    {
        var (sweeper, pass, _) = Build();
        pass.ExpiresAt.Add(Now.AddTicks(1));

        await sweeper.SweepAsync(null);

        pass.ExpiresAt.Should().ContainSingle();
    }

    [Fact]
    public async Task SweepAsync_ClockAdvancedBetweenBatches_ChangesTheNextCutoff()
    {
        var (sweeper, pass, clock) = Build(o => o.BatchSize = 1);
        pass.ExpiresAt.AddRange([Now, Now.AddMinutes(5)]);
        pass.AfterBatch = () => clock.Advance(TimeSpan.FromMinutes(10));

        var outcome = await sweeper.SweepAsync(null);

        // The first batch purges one record and is full; the second sees the advanced clock and
        // purges the record that expired in between; the third finds nothing and ends the sweep.
        pass.Cutoffs.Should().Equal(Now, Now.AddMinutes(10), Now.AddMinutes(20));
        pass.ExpiresAt.Should().BeEmpty();
        outcome.Should().Be(PollPassOutcome.WaitForInterval);
    }

    [Fact]
    public async Task SweepAsync_RecordsKeepExpiringFasterThanTheDrain_StopsAtTheBatchBoundAndPollsAgain()
    {
        var (sweeper, pass, _) = Build(o =>
        {
            o.BatchSize = 1;
            o.MaxBatchesPerSweep = 3;
        });

        // Every batch finds a fresh expired record, so no batch ever comes back short.
        pass.AfterBatch = () => pass.ExpiresAt.Add(Now);
        pass.ExpiresAt.Add(Now);

        var outcome = await sweeper.SweepAsync(null);

        pass.Cutoffs.Should().HaveCount(3);
        outcome.Should().Be(PollPassOutcome.PollAgain);
    }

    [Fact]
    public async Task SweepAsync_AgeMode_KeepsOneCutoffAcrossBatches()
    {
        var (sweeper, pass, clock) = Build(o =>
        {
            o.UseAbsoluteExpiry = false;
            o.BatchSize = 1;
        });
        pass.ExpiresAt.AddRange([Now.AddDays(-40), Now.AddDays(-40)]);
        pass.AfterBatch = () => clock.Advance(TimeSpan.FromMinutes(10));

        await sweeper.SweepAsync(TimeSpan.FromDays(30));

        pass.Cutoffs.Distinct().Should().ContainSingle().Which.Should().Be(Now.AddDays(-30));
    }

    [Fact]
    public async Task RunLoopAsync_AbsoluteExpiryWithoutRetentionPeriod_Sweeps()
    {
        var (sweeper, pass, _) = Build(o => o.RetentionPeriod = null);
        pass.ExpiresAt.Add(Now);

        using var cts = new CancellationTokenSource();
        pass.AfterBatch = () => cts.Cancel();

        await sweeper.RunLoopAsync(cts.Token);

        pass.ExpiresAt.Should().BeEmpty();
    }

    [Fact]
    public async Task RunLoopAsync_AbsoluteExpiryWithZeroRetentionPeriod_Sweeps()
    {
        var (sweeper, pass, _) = Build(o => o.RetentionPeriod = TimeSpan.Zero);
        pass.ExpiresAt.Add(Now);

        using var cts = new CancellationTokenSource();
        pass.AfterBatch = () => cts.Cancel();

        await sweeper.RunLoopAsync(cts.Token);

        pass.ExpiresAt.Should().BeEmpty();
    }
}
