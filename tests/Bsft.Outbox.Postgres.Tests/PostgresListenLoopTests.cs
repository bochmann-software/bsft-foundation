using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsft.Outbox.Postgres.Tests;

/// <summary>Tests the guards of the reconnecting loop; its behavior is exercised through the workers that run on it.</summary>
public sealed class PostgresListenLoopTests
{
    private static readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> Listen = (_, _) => Task.CompletedTask;
    private static readonly Func<CancellationToken, Task> OnNotified = _ => Task.CompletedTask;

    [Fact]
    public void Constructor_WithoutATimeProvider_Throws()
    {
        var act = () => new PostgresListenLoop(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void RunAsync_WhenARequiredArgumentIsNull_Throws()
    {
        var loop = new PostgresListenLoop(TimeProvider.System);
        var delay = TimeSpan.FromSeconds(1);

        var cases = new (string Parameter, Action Run)[]
        {
            ("listen", () => _ = loop.RunAsync(null!, OnNotified, delay, NullLogger.Instance)),
            ("onNotified", () => _ = loop.RunAsync(Listen, null!, delay, NullLogger.Instance)),
            ("logger", () => _ = loop.RunAsync(Listen, OnNotified, delay, null!)),
        };

        foreach (var (parameter, run) in cases)
            run.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }
}
