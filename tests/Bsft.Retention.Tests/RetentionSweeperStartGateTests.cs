using Bsft.Hosting.StartGates.DependencyInjection;
using Bsft.Retention.DependencyInjection;
using Bsft.Retention.Tests.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Retention.Tests;

/// <summary>
/// The sweeper behind a start gate: it does nothing until the gate opens, and what it measures from the host's
/// start (the first sweep and the startup stagger) it measures from the moment the gate opened.
/// </summary>
public sealed class RetentionSweeperStartGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Sweeper_WhenAStartGateIsPending_NeitherSweepsNorArmsItsStagger_UntilTheGateOpens()
    {
        var gate = new ControllableStartGate();
        var clock = new TimerSignallingTimeProvider(Now);
        var pass = new RecordingRetentionPass();
        var logger = new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>();
        using var host = BuildHost(gate, clock, pass, logger);

        await host.StartAsync();
        await gate.Awaited;

        logger.Entries.Should().NotContain(entry => entry.Message.Contains("waits for the host", StringComparison.Ordinal));
        pass.Calls.Should().BeEmpty();

        gate.Open();
        var due = await clock.NextTimerAsync();

        due.Should().Be(RetentionStagger.OffsetFor(BsftRetentionOptions.NameFor<RecordingRetentionPass>(), Window));
        pass.Calls.Should().BeEmpty();
        clock.Advance(due);
        await pass.FirstCall.WaitAsync(Patience);
        await host.StopAsync();
    }

    [Fact]
    public async Task Sweeper_WhenTheHostStopsWhileAStartGateIsPending_EndsWithoutSweeping()
    {
        var gate = new ControllableStartGate();
        var pass = new RecordingRetentionPass();
        using var host = BuildHost(gate, new TimerSignallingTimeProvider(Now), pass, new RecordingLogger<RetentionSweeper<RecordingRetentionPass>>());
        await host.StartAsync();
        await gate.Awaited;

        await host.StopAsync();

        pass.Calls.Should().BeEmpty();
    }

    private static IHost BuildHost(
        ControllableStartGate gate,
        TimeProvider clock,
        RecordingRetentionPass pass,
        RecordingLogger<RetentionSweeper<RecordingRetentionPass>> logger) =>
        new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(clock);
                services.AddSingleton<ILogger<RetentionSweeper<RecordingRetentionPass>>>(logger);
                services.AddScoped(_ => pass);
                services.AddBsftHostedWorkStartGate(_ => gate);
                services.AddBsftRetention<RecordingRetentionPass>(options =>
                {
                    options.RetentionPeriod = TimeSpan.FromDays(30);
                    options.MaxStartupStagger = Window;
                });
            })
            .Build();
}
