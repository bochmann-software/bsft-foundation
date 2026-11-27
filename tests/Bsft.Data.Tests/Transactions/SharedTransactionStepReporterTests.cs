using Bsft.Data.Tests.Fakes;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// The order in which several observers are told about one step, and what each kind of observer or step
/// failure does.
/// </summary>
public sealed class SharedTransactionStepReporterTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly List<string> _log = [];
    private readonly RecordingLogger<SharedTransactionStepReporter> _logger = new();

    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        FluentActions.Invoking(() => new SharedTransactionStepReporter(null!, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("observers");
        FluentActions.Invoking(() => new SharedTransactionStepReporter([], null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task RunAsync_WithSeveralObservers_StartsInRegistrationOrderAndCompletesInReverse()
    {
        var reporter = NewReporter(Observer("a"), Observer("b"));

        var result = await reporter.RunAsync(SharedTransactionStepKind.Begin, _connection, _ =>
        {
            _log.Add("step");

            return Task.FromResult(42);
        });

        result.Should().Be(42);
        _log.Should().Equal("a+Begin", "b+Begin", "step", "b-Begin", "a-Begin");
    }

    [Fact]
    public async Task RunAsync_WhenTheStepFails_CompletesTheObserversAndThrowsTheStepsException()
    {
        var reporter = NewReporter(Observer("a"));

        var act = () => reporter.RunAsync(
            SharedTransactionStepKind.Commit, _connection, _ => Task.FromException(new OperationCanceledException()));

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
        _log.Should().Equal("a+Commit", "a-Commit");
    }

    [Fact]
    public async Task RunAsync_WhenTheSecondObserverRefuses_SkipsTheStepAndCompletesOnlyTheFirst()
    {
        var refusal = new InvalidOperationException("refused");
        var second = Observer("b");
        second.Refusals[SharedTransactionStepKind.Commit] = refusal;
        var reporter = NewReporter(Observer("a"), second, Observer("c"));
        var isStepRun = false;

        var act = () => reporter.RunAsync(SharedTransactionStepKind.Commit, _connection, _ =>
        {
            isStepRun = true;

            return Task.CompletedTask;
        });

        (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Should().BeSameAs(refusal);
        isStepRun.Should().BeFalse();
        _log.Should().Equal("a+Commit", "b+Commit", "a-Commit");
    }

    [Fact]
    public async Task RunAsync_WhenAnObserverFailsOnCompletion_LogsItAndCompletesTheOthers()
    {
        var failure = new InvalidOperationException("completion failed");
        var second = Observer("b");
        second.CompletionFailure = failure;
        var reporter = NewReporter(Observer("a"), second);

        await reporter.RunAsync(SharedTransactionStepKind.Open, _connection, _ => Task.CompletedTask);

        _log.Should().Equal("a+Open", "b+Open", "b-Open", "a-Open");
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task RunAsync_WithoutObservers_RunsTheStepOnly()
    {
        var reporter = NewReporter();

        var result = await reporter.RunAsync(SharedTransactionStepKind.Begin, _connection, _ => Task.FromResult("ran"));

        result.Should().Be("ran");
        _logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RunEndingAsync_WhenObserversRefuse_RunsTheStepAndLogsEveryRefusal()
    {
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");
        var a = Observer("a");
        a.Refusals[SharedTransactionStepKind.Rollback] = first;
        var c = Observer("c");
        c.Refusals[SharedTransactionStepKind.Rollback] = second;
        var reporter = NewReporter(a, Observer("b"), c);

        await reporter.RunEndingAsync(SharedTransactionStepKind.Rollback, _connection, _ =>
        {
            _log.Add("step");

            return Task.CompletedTask;
        });

        _log.Should().Equal("a+Rollback", "b+Rollback", "c+Rollback", "step", "b-Rollback");
        _logger.Warnings.Should().Equal(first, second);
    }

    [Fact]
    public async Task RunEndingAsync_WhenTheStepFails_ThrowsItAndLogsTheRefusal()
    {
        var refusal = new InvalidOperationException("refused");
        var a = Observer("a");
        a.Refusals[SharedTransactionStepKind.Close] = refusal;
        var reporter = NewReporter(a, Observer("b"));

        var act = () => reporter.RunEndingAsync(
            SharedTransactionStepKind.Close, _connection, _ => Task.FromException(new TimeoutException()));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        _log.Should().Equal("a+Close", "b+Close", "b-Close");
        _logger.Warnings.Should().ContainSingle().Which.Should().BeSameAs(refusal);
    }

    [Fact]
    public async Task RunEndingAsync_WhenTheStepFailsWithoutARefusal_ThrowsItAndLogsNothing()
    {
        var reporter = NewReporter(Observer("a"));

        var act = () => reporter.RunEndingAsync(
            SharedTransactionStepKind.Close, _connection, _ => Task.FromException(new TimeoutException()));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        _log.Should().Equal("a+Close", "a-Close");
        _logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WithACancellationToken_HandsItToTheStep()
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = NewReporter(Observer("a"));
        var received = CancellationToken.None;

        await reporter.RunAsync(SharedTransactionStepKind.Open, _connection, token =>
        {
            received = token;

            return Task.CompletedTask;
        }, cancellation.Token);

        received.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task RunEndingAsync_WithACancellationToken_HandsItToTheStep()
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = NewReporter(Observer("a"));
        var received = CancellationToken.None;

        await reporter.RunEndingAsync(SharedTransactionStepKind.Close, _connection, token =>
        {
            received = token;

            return Task.CompletedTask;
        }, cancellation.Token);

        received.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task RunEndingAsync_WithoutARefusal_CompletesTheObserversAndLogsNothing()
    {
        var reporter = NewReporter(Observer("a"));

        await reporter.RunEndingAsync(SharedTransactionStepKind.Release, _connection, _ => Task.CompletedTask);

        _log.Should().Equal("a+Release", "a-Release");
        _logger.Warnings.Should().BeEmpty();
    }

    public void Dispose() => _connection.Dispose();

    private FakeSharedTransactionStepObserver Observer(string name) => new(_log, name);

    private SharedTransactionStepReporter NewReporter(params ISharedTransactionStepObserver[] observers) =>
        new(observers, _logger);
}
