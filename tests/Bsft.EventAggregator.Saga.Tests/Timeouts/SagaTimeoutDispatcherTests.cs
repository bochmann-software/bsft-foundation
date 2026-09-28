using System.Text.Json;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Tests.Fakes;
using Bsft.EventAggregator.Saga.Tests.Support;
using Bsft.EventAggregator.Saga.Timeouts;
using Bsft.Instrumentation;
using Bsft.Instrumentation.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

public class SagaTimeoutDispatcherTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemorySagaTimeoutStore _store = new();
    private readonly SimpleEventAggregator _aggregator = new();
    private readonly FixedTimeProvider _time = new(_now);
    private readonly BsftSagaOptions _root = new();

    // The dispatcher reads its settings through IOptions<BsftSagaOptions>.Timeouts, and the group is
    // get-only, so the tests mutate the group in place and hand the root options to the constructor.
    private readonly SagaTimeoutOptions _options;
    private readonly ISagaTelemetry _telemetry = Substitute.For<ISagaTelemetry>();

    // Reads the same ambient slot the publisher writes, through the public abstraction.
    private readonly ISagaCorrelationScope _correlationScope = new SagaCorrelationScope();

    private readonly ISagaTimeoutPublisher _publisher;

    public SagaTimeoutDispatcherTests()
    {
        _options = _root.Timeouts;
        _options.PollInterval = TimeSpan.FromMilliseconds(1);
        _publisher = new SagaTimeoutPublisher(_aggregator, _correlationScope);
    }

    [Fact]
    public async Task ADueTimeout_PublishesItsEvent()
    {
        var received = new List<QuoteFollowUpDue>();
        _aggregator.Subscribe<QuoteFollowUpDue>(e => { received.Add(e); return Task.CompletedTask; });
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        var handled = await CreateDispatcher().ProcessOnceAsync();

        handled.Should().Be(1);
        received.Should().ContainSingle().Which.QuoteId.Should().Be("quote-1");
        _telemetry.Received(1).TimeoutFired("Sales");
    }

    [Fact]
    public async Task DrainDueAsync_RunsTheSamePassAsThePollingLoop()
    {
        var received = new List<QuoteFollowUpDue>();
        _aggregator.Subscribe<QuoteFollowUpDue>(e => { received.Add(e); return Task.CompletedTask; });
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        ISagaTimeoutDrainer drainer = CreateDispatcher();
        var handled = await drainer.DrainDueAsync();

        handled.Should().Be(1);
        received.Should().ContainSingle().Which.QuoteId.Should().Be("quote-1");
    }

    [Fact]
    public async Task DrainDueAsync_WithNothingDue_ReturnsZero()
    {
        await ScheduleAsync("followup", _now.AddHours(1));

        ISagaTimeoutDrainer drainer = CreateDispatcher();

        (await drainer.DrainDueAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AFiringTimeout_OpensATraceSpanAroundThePublish()
    {
        using var capture = new SagaTelemetryCapture("Bsft.Saga");
        var dispatcher = new SagaTimeoutDispatcher(
            _store, _publisher, _time, Options.Create(_root), BuildRealTelemetry(), NullLogger<SagaTimeoutDispatcher>.Instance);
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        await dispatcher.ProcessOnceAsync();

        var span = capture.Activities.Should().ContainSingle(a => a.OperationName == "saga.timeout").Subject;
        span.GetTagItem("saga.type").Should().Be("Sales");
        span.GetTagItem("saga.timeout.name").Should().Be("followup");
    }

    [Fact]
    public async Task AFiringTimeout_KeepsItsCorrelationIdInsideTheSpan()
    {
        string? correlationInsidePublish = null;
        _aggregator.Subscribe<QuoteFollowUpDue>(_ =>
        {
            correlationInsidePublish = _correlationScope.Current;
            return Task.CompletedTask;
        });
        await ScheduleAsync("followup", _now.AddMinutes(-1), correlationId: "corr-42");

        await CreateDispatcher().ProcessOnceAsync();

        // The span wraps the correlation scope, so the woken handler still sees the id of the
        // request that scheduled the reminder.
        correlationInsidePublish.Should().Be("corr-42");
    }

    private static ISagaTelemetry BuildRealTelemetry()
    {
        var services = new ServiceCollection();
        services.AddBsftInstrumentationSource("Bsft.Saga");
        services.AddBsftSaga();

        return services.BuildServiceProvider().GetRequiredService<ISagaTelemetry>();
    }

    [Fact]
    public async Task ATimeoutThatIsNotDue_IsLeftAlone()
    {
        await ScheduleAsync("followup", _now.AddHours(1));

        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ADispatchedTimeout_IsNotPublishedTwice()
    {
        var received = 0;
        _aggregator.Subscribe<QuoteFollowUpDue>(_ => { received++; return Task.CompletedTask; });
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        var dispatcher = CreateDispatcher();
        await dispatcher.ProcessOnceAsync();
        await dispatcher.ProcessOnceAsync();

        received.Should().Be(1);
    }

    [Fact]
    public async Task TheCorrelationIdOfTheSchedulingRequest_IsRestoredDuringThePublish()
    {
        string? seen = null;
        _aggregator.Subscribe<QuoteFollowUpDue>(_ => { seen = _correlationScope.Current; return Task.CompletedTask; });
        await ScheduleAsync("followup", _now.AddMinutes(-1), correlationId: "corr-42");

        await CreateDispatcher().ProcessOnceAsync();

        seen.Should().Be("corr-42");
        _correlationScope.Current.Should().BeNull("the ambient id must not leak past the dispatch");
    }

    [Fact]
    public async Task SkipBeyondMaxAge_DiscardsStaleTimeoutsAndFiresRecentOnes()
    {
        _options.CatchUpPolicy = SagaTimeoutCatchUpPolicy.SkipBeyondMaxAge;
        _options.MaxCatchUpAge = TimeSpan.FromHours(2);
        var received = new List<QuoteFollowUpDue>();
        _aggregator.Subscribe<QuoteFollowUpDue>(e => { received.Add(e); return Task.CompletedTask; });

        await ScheduleAsync("stale", _now.AddDays(-3), quoteId: "old");
        await ScheduleAsync("fresh", _now.AddMinutes(-30), quoteId: "new");

        var handled = await CreateDispatcher().ProcessOnceAsync();

        handled.Should().Be(2);
        received.Should().ContainSingle().Which.QuoteId.Should().Be("new");
        _telemetry.Received(1).TimeoutMissed("Sales");
    }

    [Fact]
    public async Task FireLatestPerSaga_KeepsOnlyTheNewestReminderOfOneSaga()
    {
        _options.CatchUpPolicy = SagaTimeoutCatchUpPolicy.FireLatestPerSaga;
        var received = new List<QuoteFollowUpDue>();
        _aggregator.Subscribe<QuoteFollowUpDue>(e => { received.Add(e); return Task.CompletedTask; });

        await ScheduleAsync("first", _now.AddDays(-3), quoteId: "older");
        await ScheduleAsync("second", _now.AddDays(-1), quoteId: "newer");
        await ScheduleAsync("other", _now.AddDays(-2), quoteId: "other-saga", correlationKey: "quote-2");

        await CreateDispatcher().ProcessOnceAsync();

        received.Select(e => e.QuoteId).Should().BeEquivalentTo(["newer", "other-saga"]);
    }

    [Fact]
    public async Task FireAll_IsTheDefaultAndReplaysTheWholeBacklog()
    {
        var received = new List<QuoteFollowUpDue>();
        _aggregator.Subscribe<QuoteFollowUpDue>(e => { received.Add(e); return Task.CompletedTask; });

        await ScheduleAsync("first", _now.AddDays(-3), quoteId: "a");
        await ScheduleAsync("second", _now.AddDays(-1), quoteId: "b");

        await CreateDispatcher().ProcessOnceAsync();

        received.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnUnresolvableEventType_IsCountedAsAFailedAttempt()
    {
        await ScheduleAsync("broken", _now.AddMinutes(-1), payloadTypeName: "Nope.NotAType, Nowhere");

        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(0);

        (await _store.ListDueAsync(_now, 10))[0].AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task APayloadThatDeserializesToNull_IsCountedAsAFailedAttempt()
    {
        await ScheduleAsync("broken", _now.AddMinutes(-1), payloadJson: "null");

        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(0);

        (await _store.ListDueAsync(_now, 10))[0].AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task AFailingSubscriber_LeavesTheTimeoutPendingForRetry()
    {
        _aggregator.Subscribe<QuoteFollowUpDue>(_ => throw new InvalidOperationException("handler down"));
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        (await CreateDispatcher().ProcessOnceAsync()).Should().Be(0);

        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task RunLoop_DrainsTheStoreAndStopsOnCancellation()
    {
        var received = new TaskCompletionSource();
        _aggregator.Subscribe<QuoteFollowUpDue>(_ => { received.TrySetResult(); return Task.CompletedTask; });
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        using var cts = new CancellationTokenSource();
        var loop = CreateDispatcher().RunLoopAsync(cts.Token);

        await received.Task;
        await cts.CancelAsync();
        await loop;

        loop.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task RunLoop_ExitsImmediatelyOnAnAlreadyCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await CreateDispatcher().RunLoopAsync(cts.Token);
    }

    [Fact]
    public async Task RunLoop_SurvivesAFailingPass()
    {
        var store = new ThrowingTimeoutStore();
        var dispatcher = new SagaTimeoutDispatcher(
            store, _publisher, _time, Options.Create(_root), _telemetry, NullLogger<SagaTimeoutDispatcher>.Instance);

        using var cts = new CancellationTokenSource();
        var loop = dispatcher.RunLoopAsync(cts.Token);

        await store.FirstFailure.Task;
        await cts.CancelAsync();
        await loop;

        store.Calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunLoop_KeepsPollingAfterAPassCancelledByAForeignToken()
    {
        var store = new ForeignCancelingTimeoutStore();
        var dispatcher = new SagaTimeoutDispatcher(
            store, _publisher, _time, Options.Create(_root), _telemetry, NullLogger<SagaTimeoutDispatcher>.Instance);

        using var cts = new CancellationTokenSource();
        var loop = dispatcher.RunLoopAsync(cts.Token);

        // A cancellation that did not come from the stopping token must not be read as a shutdown:
        // the host would stay up with no saga timer ever firing again. Racing the loop against the
        // next poll rather than awaiting it keeps a regression a failed assertion, not a hang.
        await Task.WhenAny(store.PolledAgain.Task, loop);

        store.PolledAgain.Task.IsCompletedSuccessfully.Should()
            .BeTrue("a foreign cancellation is a failed pass, not a reason to end the polling loop");

        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task APublishCancelledByItsOwnToken_IsCountedAsAFailedAttempt()
    {
        using var foreign = new CancellationTokenSource();
        await foreign.CancelAsync();

        // Stands in for the usual case: the woken step calls an HttpClient whose own timeout expires,
        // and the TaskCanceledException travels back out through the store's dispatch callback.
        var publisher = Substitute.For<ISagaTimeoutPublisher>();
        publisher.PublishAsync(Arg.Any<SagaTimeout>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new TaskCanceledException("the request timed out", null, foreign.Token));

        var dispatcher = new SagaTimeoutDispatcher(
            _store, publisher, _time, Options.Create(_root), _telemetry, NullLogger<SagaTimeoutDispatcher>.Instance);
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        (await dispatcher.ProcessOnceAsync()).Should().Be(0);

        // The timeout is identifiable here, so the pass must count the attempt against it rather than
        // read the cancellation as a shutdown and unwind.
        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ADispatchPassCancelledByItsOwnToken_DoesNotCancelTheCaller()
    {
        using var foreign = new CancellationTokenSource();
        await foreign.CancelAsync();

        var publisher = Substitute.For<ISagaTimeoutPublisher>();
        publisher.PublishAsync(Arg.Any<SagaTimeout>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new TaskCanceledException("the request timed out", null, foreign.Token));

        ISagaTimeoutDrainer drainer = new SagaTimeoutDispatcher(
            _store, publisher, _time, Options.Create(_root), _telemetry, NullLogger<SagaTimeoutDispatcher>.Instance);
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        // The drain seam is the saga harness's entry point; a foreign cancellation must surface as a
        // pass that fired nothing, not as a cancellation of the test that asked for the drain.
        await FluentActions.Awaiting(() => drainer.DrainDueAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task APoisonTimeout_IsRetiredOnceItHasUsedUpItsAttempts()
    {
        await ScheduleAsync("broken", _now.AddMinutes(-1), payloadTypeName: "Nope.NotAType, Nowhere");
        var dispatcher = CreateDispatcher();

        for (var attempt = 0; attempt < _options.MaxDispatchAttempts; attempt++)
        {
            await dispatcher.ProcessOnceAsync();
        }

        // Without a bound the row keeps its place at the head of every poll, and once a batch of them
        // has accumulated no saga timer in the application fires again.
        (await _store.ListDueAsync(_now, 10)).Should().BeEmpty();
        _telemetry.Received(1).TimeoutDeadLettered("Sales");
    }

    [Fact]
    public async Task ARetiredTimeout_FreesItsNameAndKeepsWhyItFailed()
    {
        _options.MaxDispatchAttempts = 1;
        await ScheduleAsync("broken", _now.AddMinutes(-1), payloadTypeName: "Nope.NotAType, Nowhere");

        await CreateDispatcher().ProcessOnceAsync();

        // Retiring stamps DispatchedAt as well, so the name is pending no more and the saga can
        // schedule it again; the retirement itself stays legible next to it.
        await ScheduleAsync("broken", _now.AddMinutes(-1));
        var due = await _store.ListDueAsync(_now, 10);

        due.Should().ContainSingle().Which.AttemptCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AMaxDispatchAttemptsBelowOneIsRejectedAtStartup(int maxDispatchAttempts)
    {
        _options.MaxDispatchAttempts = maxDispatchAttempts;

        // Otherwise the very first failure would retire a timeout that a retry would have delivered.
        FluentActions.Invoking(CreateDispatcher).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ATransientFailure_LeavesTheTimeoutPendingRatherThanRetiringIt()
    {
        var attempts = 0;
        _aggregator.Subscribe<QuoteFollowUpDue>(_ =>
        {
            attempts++;
            return attempts < 3 ? throw new InvalidOperationException("handler down") : Task.CompletedTask;
        });
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        var dispatcher = CreateDispatcher();
        await dispatcher.ProcessOnceAsync();
        await dispatcher.ProcessOnceAsync();

        // At-least-once has to survive a handler that is merely down for a while.
        (await _store.ListDueAsync(_now, 10)).Should().ContainSingle().Which.AttemptCount.Should().Be(2);

        (await dispatcher.ProcessOnceAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ABatchSizeBelowOneIsRejectedAtStartup(int batchSize)
    {
        _options.BatchSize = batchSize;

        // Otherwise every poll would return nothing and timers would simply never fire.
        FluentActions.Invoking(CreateDispatcher).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ACatchUpPolicyThatNamesNothingIsRejectedAtStartup()
    {
        _options.CatchUpPolicy = (SagaTimeoutCatchUpPolicy)(-1);

        // Otherwise a missed timeout would be answered by whichever branch the selection falls through
        // to, and the options validator already refuses this, which said it was no stricter.
        FluentActions.Invoking(CreateDispatcher).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANegativePollIntervalIsRejectedAtStartup()
    {
        _options.PollInterval = TimeSpan.FromSeconds(-1);

        // Otherwise Task.Delay throws inside the loop and takes the host down with it.
        FluentActions.Invoking(CreateDispatcher).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANegativeMaxCatchUpAgeIsRejectedAtStartup()
    {
        _options.MaxCatchUpAge = TimeSpan.FromDays(-1);

        FluentActions.Invoking(CreateDispatcher).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        var logger = NullLogger<SagaTimeoutDispatcher>.Instance;

        FluentActions.Invoking(() => new SagaTimeoutDispatcher(null!, _publisher, _time, Options.Create(_root), _telemetry, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        FluentActions.Invoking(() => new SagaTimeoutDispatcher(_store, null!, _time, Options.Create(_root), _telemetry, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("publisher");
        FluentActions.Invoking(() => new SagaTimeoutDispatcher(_store, _publisher, null!, Options.Create(_root), _telemetry, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        FluentActions.Invoking(() => new SagaTimeoutDispatcher(_store, _publisher, _time, null!, _telemetry, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => new SagaTimeoutDispatcher(_store, _publisher, _time, Options.Create(_root), null!, logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        FluentActions.Invoking(() => new SagaTimeoutDispatcher(_store, _publisher, _time, Options.Create(_root), _telemetry, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task StartAsync_RunsTheLoopAsAHostedService()
    {
        var received = new TaskCompletionSource();
        _aggregator.Subscribe<QuoteFollowUpDue>(_ => { received.TrySetResult(); return Task.CompletedTask; });
        await ScheduleAsync("followup", _now.AddMinutes(-1));

        var dispatcher = CreateDispatcher();
        await dispatcher.StartAsync(CancellationToken.None);

        await received.Task;
        await dispatcher.StopAsync(CancellationToken.None);
        dispatcher.Dispose();
    }

    private SagaTimeoutDispatcher CreateDispatcher() =>
        new(_store, _publisher, _time, Options.Create(_root), _telemetry, NullLogger<SagaTimeoutDispatcher>.Instance);

    private Task ScheduleAsync(
        string name,
        DateTimeOffset dueAt,
        string quoteId = "quote-1",
        string correlationKey = "quote-1",
        string? correlationId = null,
        string? payloadTypeName = null,
        string? payloadJson = null) =>
        _store.ScheduleAsync(new SagaTimeout
        {
            SagaType = "Sales",
            CorrelationKey = correlationKey,
            Name = name,
            StepIndex = 1,
            DueAt = dueAt,
            PayloadJson = payloadJson ?? JsonSerializer.Serialize(new QuoteFollowUpDue(quoteId)),
            PayloadTypeName = payloadTypeName ?? typeof(QuoteFollowUpDue).AssemblyQualifiedName!,
            CorrelationId = correlationId,
            ConcurrencyToken = Guid.NewGuid(),
        });

    public sealed record QuoteFollowUpDue(string QuoteId);

    private sealed class ThrowingTimeoutStore : ISagaTimeoutStore
    {
        public TaskCompletionSource FirstFailure { get; } = new();

        public int Calls { get; private set; }

        public Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            Calls++;
            FirstFailure.TrySetResult();
            throw new InvalidOperationException("store unavailable");
        }

        public Task ScheduleAsync(SagaTimeout timeout, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> TryDispatchAsync(SagaTimeout timeout, DateTimeOffset dispatchedAt, Func<SagaTimeout, CancellationToken, Task> dispatch, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordFailedAttemptAsync(long id, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Fails its first poll with a cancellation raised by a token of its own — a store call that timed
    /// out — and reports every later poll, so a test can tell whether the loop survived it.
    /// </summary>
    private sealed class ForeignCancelingTimeoutStore : ISagaTimeoutStore
    {
        private int _calls;

        public TaskCompletionSource PolledAgain { get; } = new();

        public Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                using var foreign = new CancellationTokenSource();
                foreign.Cancel();

                throw new OperationCanceledException("the store call timed out", foreign.Token);
            }

            PolledAgain.TrySetResult();

            return Task.FromResult<IReadOnlyList<SagaTimeout>>([]);
        }

        public Task ScheduleAsync(SagaTimeout timeout, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> TryDispatchAsync(SagaTimeout timeout, DateTimeOffset dispatchedAt, Func<SagaTimeout, CancellationToken, Task> dispatch, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordFailedAttemptAsync(long id, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }
}
