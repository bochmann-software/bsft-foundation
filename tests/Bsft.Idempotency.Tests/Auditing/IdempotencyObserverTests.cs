using Bsft.Idempotency.Auditing;
using Bsft.Idempotency.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests.Auditing;

public class IdempotencyObserverTests
{
    private readonly RecordingAuditSink _sink = new();
    private readonly RecordingTelemetry _telemetry = new();
    private readonly MutableTimeProvider _clock = new();

    [Theory]
    [InlineData(IdempotencyAuditAction.Replayed)]
    [InlineData(IdempotencyAuditAction.KeyReused)]
    [InlineData(IdempotencyAuditAction.InProgress)]
    public async Task ReportAsync_CountsTheDecisionAndHandsItToTheAuditSinkStampedFromTheClock(
        IdempotencyAuditAction action)
    {
        _clock.Advance(TimeSpan.FromMinutes(7));

        await Observer().ReportAsync(action, "scope-digest", "key");

        _telemetry.Actions.Should().Equal(action);
        _sink.Events.Should().ContainSingle().Which.Should().Be(
            new IdempotencyAuditEvent(action, "scope-digest", "key", _clock.GetUtcNow()));
    }

    [Fact]
    public void Constructor_NullAuditSink_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyObserver(null!, _telemetry, _clock))
            .Should().Throw<ArgumentNullException>().WithParameterName("auditSink");

    [Fact]
    public void Constructor_NullTelemetry_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyObserver(_sink, null!, _clock))
            .Should().Throw<ArgumentNullException>().WithParameterName("telemetry");

    [Fact]
    public void Constructor_NullTimeProvider_IsRefused() =>
        FluentActions.Invoking(() => new IdempotencyObserver(_sink, _telemetry, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");

    [Fact]
    public async Task ReportAsync_NullScope_IsRefused() =>
        await FluentActions
            .Awaiting(() => Observer().ReportAsync(IdempotencyAuditAction.Replayed, null!, "key"))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("scope");

    [Fact]
    public async Task ReportAsync_NullKey_IsRefused() =>
        await FluentActions
            .Awaiting(() => Observer().ReportAsync(IdempotencyAuditAction.Replayed, "scope", null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("key");

    private IdempotencyObserver Observer() => new(_sink, _telemetry, _clock);
}
