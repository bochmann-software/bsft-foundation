using Bsft.Hosting.Logging;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.Tests.Logging;

public sealed class LoggingObserverFailureReporterTests
{
    private readonly RecordingLogger<LoggingObserverFailureReporter> _logger = new();

    [Fact]
    public void ReportObserverFailed_ByDefault_LogsTheObserverAndTheFailureTypeButNotItsPayload()
    {
        var failure = new InvalidOperationException("password=hunter2 leaked");

        Reporter().ReportObserverFailed(new FakeObserver(), failure);

        var entry = _logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(FakeObserver)).And.Contain(nameof(InvalidOperationException));
        entry.Message.Should().NotContain("hunter2");
        entry.Exception.Should().BeNull();
    }

    [Fact]
    public void ReportObserverFailed_WhenExceptionDetailsAreOn_LogsTheException()
    {
        var failure = new InvalidOperationException("observer failed");

        Reporter(new ObserverFailureLoggingOptions { IncludeExceptionDetails = true }).ReportObserverFailed(new FakeObserver(), failure);

        var entry = _logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(FakeObserver));
        entry.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public void Arguments_AreValidated()
    {
        FluentActions.Invoking(() => new LoggingObserverFailureReporter(null!, Options.Create(new ObserverFailureLoggingOptions()))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new LoggingObserverFailureReporter(_logger, null!)).Should().Throw<ArgumentNullException>();
        var reporter = Reporter();
        FluentActions.Invoking(() => reporter.ReportObserverFailed(null!, new InvalidOperationException())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => reporter.ReportObserverFailed(this, null!)).Should().Throw<ArgumentNullException>();
    }

    private LoggingObserverFailureReporter Reporter(ObserverFailureLoggingOptions? options = null) =>
        new(_logger, Options.Create(options ?? new ObserverFailureLoggingOptions()));

    private sealed class FakeObserver;
}
