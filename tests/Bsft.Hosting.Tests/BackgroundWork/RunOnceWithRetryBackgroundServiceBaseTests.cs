using Bsft.Hosting.BackgroundWork;
using Bsft.Hosting.Tests.BackgroundWork.Fakes;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Hosting.Tests.BackgroundWork;

public sealed class RunOnceWithRetryBackgroundServiceBaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly RecordingTimeProvider _clock = new(Now);
    private readonly RecordingLogger<RecordingRunOnceService> _logger = new();

    [Fact]
    public async Task RunUntilSucceededAsync_FirstAttemptSucceeds_MakesNoFurtherAttemptAndWaitsForNothing()
    {
        var service = Build();

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(1);
        _clock.Delays.Should().BeEmpty();
        _logger.Messages.Should().ContainSingle(message => message.Contains("succeeded on attempt 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AFailedAttempt_IsRetriedAfterTheInitialDelay()
    {
        var service = Build(options => options.InitialRetryDelay = TimeSpan.FromSeconds(4));
        service.ThenFails(new InvalidOperationException("the database is not up yet"));

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(2);
        _clock.Delays.Should().Equal(TimeSpan.FromSeconds(4));
        _logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Error && entry.Exception != null);
    }

    [Fact]
    public async Task RunUntilSucceededAsync_TheDelayGrowsByTheFactorAndStopsAtTheCap()
    {
        var service = Build(options =>
        {
            options.InitialRetryDelay = TimeSpan.FromSeconds(1);
            options.BackoffFactor = 2;
            options.MaxRetryDelay = TimeSpan.FromSeconds(3);
        });

        service.ThenFails(new InvalidOperationException())
            .ThenFails(new InvalidOperationException())
            .ThenFails(new InvalidOperationException())
            .ThenFails(new InvalidOperationException());

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(5);
        _clock.Delays.Should().Equal(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_ADelayThatWouldOverflowIsCappedRatherThanThrown()
    {
        // TimeSpan * double throws on overflow, which would take the service down on a later attempt
        // instead of arriving at the cap.
        var service = Build(options =>
        {
            options.InitialRetryDelay = TimeSpan.FromDays(40);
            options.BackoffFactor = 1e15;
            options.MaxRetryDelay = TimeSpan.FromDays(49);
        });

        service.ThenFails(new InvalidOperationException()).ThenFails(new InvalidOperationException());

        await service.RunUntilSucceededAsync();

        _clock.Delays.Should().Equal(TimeSpan.FromDays(40), TimeSpan.FromDays(49));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AnAttemptThatThrowsBeforeReturningATask_IsAFailedAttemptAndNotACrash()
    {
        // The failure mode the base class exists for: a synchronous throw escaping ExecuteAsync takes
        // the host down under the default BackgroundServiceExceptionBehavior.
        var service = Build();
        service.ThenThrowsSynchronously(new InvalidOperationException("the connection string is missing"));

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(2);
        _logger.Messages.Should().Contain(message => message.Contains("failed on attempt 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AnAttemptCancelledWithoutAStop_IsAFailedAttemptAndSaysSo()
    {
        var service = Build();
        service.ThenCancelsItself();

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(2);
        _logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Error
            && entry.Exception == null
            && entry.Message.Contains("although the host was not stopping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AStopDuringTheBackoff_UnwindsWithoutAnotherAttempt()
    {
        using var stopping = new CancellationTokenSource();
        var service = Build();
        service.Then(_ =>
        {
            stopping.Cancel();

            return Task.FromException(new InvalidOperationException());
        });

        await service.RunUntilSucceededAsync(stopping.Token);

        service.AttemptCount.Should().Be(1);
        _logger.Messages.Should().Contain(message => message.Contains("is stopping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AnAttemptCancelledByTheStop_ReportsNothingButTheStop()
    {
        using var stopping = new CancellationTokenSource();
        var service = Build();
        service.Then(_ =>
        {
            stopping.Cancel();

            return Task.FromCanceled(new CancellationToken(true));
        });

        await service.RunUntilSucceededAsync(stopping.Token);

        service.AttemptCount.Should().Be(1);
        _logger.Levels.Should().NotContain(LogLevel.Error);
        _logger.Messages.Should().Contain(message => message.Contains("is stopping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AnAttemptBudget_GivesUpOnceItIsSpent()
    {
        var service = Build(options => options.MaxAttempts = 2);
        service.ThenFails(new InvalidOperationException()).ThenFails(new InvalidOperationException());

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(2);
        _clock.Delays.Should().ContainSingle();
        _logger.Messages.Should().Contain(message => message.Contains("gave up after 2 attempts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AStartupDelay_IsWaitedOutBeforeTheFirstAttempt()
    {
        var service = Build(options => options.StartupDelay = TimeSpan.FromSeconds(30));

        await service.RunUntilSucceededAsync();

        service.AttemptCount.Should().Be(1);
        _clock.Delays.Should().Equal(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task RunUntilSucceededAsync_AHostAlreadyStopping_AttemptsNothingAtAll()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var service = Build();

        await service.RunUntilSucceededAsync(stopping.Token);

        service.AttemptCount.Should().Be(0);
        _logger.Messages.Should().ContainSingle(message => message.Contains("is stopping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_IsTheLoopTheHostedServiceContractStarts()
    {
        var service = Build();

        await service.StartAsync(CancellationToken.None);
        // Since Microsoft.Extensions.Hosting 10, StartAsync runs the whole of ExecuteAsync on the
        // thread pool, so a stop right after the start can come before the first attempt. The test
        // waits for it rather than relying on the synchronous start of earlier versions.
        SpinWait.SpinUntil(() => service.AttemptCount > 0, TimeSpan.FromSeconds(10)).Should().BeTrue();
        await service.StopAsync(CancellationToken.None);

        service.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void TheOptionsAreReadFromTheRuntimeTypeOfTheDerivedService()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftRunOnceOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(new BsftRunOnceOptions());

        _ = new RecordingRunOnceService(monitor, _clock, NullLogger<RecordingRunOnceService>.Instance);

        monitor.Received(1).Get(typeof(RecordingRunOnceService).FullName);
    }

    [Fact]
    public void TheConstructorRejectsEveryMissingDependency()
    {
        var monitor = Substitute.For<IOptionsMonitor<BsftRunOnceOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(new BsftRunOnceOptions());

        ((Action)(() => new RecordingRunOnceService(null!, _clock, NullLogger<RecordingRunOnceService>.Instance)))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        ((Action)(() => new RecordingRunOnceService(monitor, null!, NullLogger<RecordingRunOnceService>.Instance)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        ((Action)(() => new RecordingRunOnceService(monitor, _clock, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    private RecordingRunOnceService Build(Action<BsftRunOnceOptions>? configure = null)
    {
        var options = new BsftRunOnceOptions { InitialRetryDelay = TimeSpan.FromSeconds(1) };
        configure?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<BsftRunOnceOptions>>();
        monitor.Get(Arg.Any<string?>()).Returns(options);

        return new RecordingRunOnceService(monitor, _clock, _logger);
    }
}
