using Bsft.Calendar;
using Bsft.EventAggregator.Saga.Diagnostics;
using Bsft.EventAggregator.Saga.Tests.Fakes;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Timeouts;

public class SagaTimeoutSchedulerTests
{
    private static readonly DateTimeOffset _fridayNoon = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemorySagaTimeoutStore _store = new();
    private readonly FixedTimeProvider _time = new(_fridayNoon);
    private readonly ISagaTelemetry _telemetry = Substitute.For<ISagaTelemetry>();
    private readonly BsftCalendarOptions _calendarOptions = new();

    [Fact]
    public async Task Schedule_WithADelay_UsesTheDefaultCalendar()
    {
        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2)));

        var stored = await SinglePendingAsync();
        stored.DueAt.Should().Be(_fridayNoon.AddDays(2));
        _telemetry.Received(1).TimeoutScheduled("Sales");
    }

    [Fact]
    public async Task Schedule_WithACalendarKey_ResolvesThatCalendarPerSagaInstance()
    {
        _calendarOptions.AddCalendar("berlin", new WeekdayBusinessCalendar());

        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2), calendarKey: "berlin"));

        // Friday plus two business days is Tuesday, not Sunday.
        (await SinglePendingAsync()).DueAt.Should().Be(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_WithAnAbsoluteDueDate_BypassesTheCalendar()
    {
        _calendarOptions.Default = new WeekdayBusinessCalendar();
        var sunday = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);

        await FlushAsync(new PendingSagaTimeout
        {
            Kind = PendingSagaTimeoutKind.Schedule,
            Name = "followup",
            DueAt = sunday,
            Payload = new QuoteFollowUpDue("quote-1"),
        });

        (await SinglePendingAsync()).DueAt.Should().Be(sunday);
    }

    [Fact]
    public async Task Schedule_PersistsThePayloadAndItsType()
    {
        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2)));

        var stored = await SinglePendingAsync();
        stored.PayloadTypeName.Should().Be(typeof(QuoteFollowUpDue).AssemblyQualifiedName);
        stored.PayloadJson.Should().Contain("quote-1");
        stored.StepIndex.Should().Be(1);
        stored.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task Cancel_RemovesTheNamedTimeoutAndIsCounted()
    {
        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2)));

        await FlushAsync(new PendingSagaTimeout { Kind = PendingSagaTimeoutKind.Cancel, Name = "followup" });

        (await _store.ListDueAsync(_fridayNoon.AddDays(30), 10)).Should().BeEmpty();
        _telemetry.Received(1).TimeoutCanceled("Sales");
    }

    [Fact]
    public async Task CancelAll_ClearsTheInstanceAndIsCounted()
    {
        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2)), Schedule("callback", delay: TimeSpan.FromDays(3)));

        await FlushAsync(new PendingSagaTimeout { Kind = PendingSagaTimeoutKind.CancelAll });

        (await _store.ListDueAsync(_fridayNoon.AddDays(30), 10)).Should().BeEmpty();
        _telemetry.Received(1).TimeoutCanceled("Sales");
    }

    [Fact]
    public async Task CancelForStepAsync_DropsTheRemindersOfTheStepBeingLeft()
    {
        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2)));

        await CreateScheduler().CancelForStepAsync("Sales", "quote-1", 1);

        (await _store.ListDueAsync(_fridayNoon.AddDays(30), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAllAsync_DropsEverythingOfACompletedSaga()
    {
        await FlushAsync(Schedule("followup", delay: TimeSpan.FromDays(2)));

        await CreateScheduler().CancelAllAsync("Sales", "quote-1");

        (await _store.ListDueAsync(_fridayNoon.AddDays(30), 10)).Should().BeEmpty();
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        var calendars = new BusinessCalendarProviderStub();

        FluentActions.Invoking(() => new SagaTimeoutScheduler(null!, calendars, _time, _telemetry, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");
        FluentActions.Invoking(() => new SagaTimeoutScheduler(_store, null!, _time, _telemetry, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("calendars");
        FluentActions.Invoking(() => new SagaTimeoutScheduler(_store, calendars, null!, _telemetry, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        FluentActions.Invoking(() => new SagaTimeoutScheduler(_store, calendars, _time, null!, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
        FluentActions.Invoking(() => new SagaTimeoutScheduler(_store, calendars, _time, _telemetry, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task AFailedTimeoutWriteIsRecordedBeforeItPropagates()
    {
        var logger = new RecordingLogger();
        var scheduler = new SagaTimeoutScheduler(new ThrowingStore(), new BusinessCalendarProviderStub(), _time, _telemetry, logger);

        var act = async () => await scheduler.FlushAsync("Sales", "quote-1", 1, [Schedule("followup", TimeSpan.FromDays(2))]);

        // The step's state is already committed by now, so a lost reminder must leave a trace.
        await act.Should().ThrowAsync<InvalidOperationException>();

        var recorded = logger.Errors.Should().ContainSingle().Subject;
        recorded.Should().Contain("Sales").And.Contain("followup");
        recorded.Should().NotContain("quote-1", "the correlation key is business data and stays out of the log");
    }

    private static PendingSagaTimeout Schedule(string name, TimeSpan delay, string? calendarKey = null) => new()
    {
        Kind = PendingSagaTimeoutKind.Schedule,
        Name = name,
        Delay = delay,
        Payload = new QuoteFollowUpDue("quote-1"),
        CalendarKey = calendarKey,
        CorrelationId = "corr-1",
    };

    private readonly ILogger<SagaTimeoutScheduler> _logger = NullLogger<SagaTimeoutScheduler>.Instance;

    private SagaTimeoutScheduler CreateScheduler() =>
        new(_store, new BusinessCalendarProviderStub(_calendarOptions), _time, _telemetry, _logger);

    private Task FlushAsync(params PendingSagaTimeout[] operations) =>
        CreateScheduler().FlushAsync("Sales", "quote-1", 1, operations);

    private async Task<SagaTimeout> SinglePendingAsync()
    {
        var due = await _store.ListDueAsync(_fridayNoon.AddDays(30), 10);
        return due.Should().ContainSingle().Subject;
    }

    private sealed record QuoteFollowUpDue(string QuoteId);

    private sealed class ThrowingStore : ISagaTimeoutStore
    {
        public Task ScheduleAsync(SagaTimeout timeout, CancellationToken ct = default) =>
            throw new InvalidOperationException("store unavailable");

        public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SagaTimeout>>([]);

        public Task<bool> TryDispatchAsync(SagaTimeout timeout, DateTimeOffset dispatchedAt, Func<SagaTimeout, CancellationToken, Task> dispatch, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset skippedAt, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordFailedAttemptAsync(long id, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeadLetterAsync(long id, DateTimeOffset deadLetteredAt, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : ILogger<SagaTimeoutScheduler>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    private sealed class BusinessCalendarProviderStub : IBusinessCalendarProvider
    {
        private readonly BsftCalendarOptions _options;

        public BusinessCalendarProviderStub(BsftCalendarOptions? options = null) => _options = options ?? new BsftCalendarOptions();

        public IBusinessCalendar Get(string? calendarKey) =>
            calendarKey is null ? _options.Default : _options.Calendars[calendarKey];
    }
}
