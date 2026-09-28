using Bsft.Idempotency.Replay;
using Bsft.Idempotency.Tests.Fakes;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class IdempotencyGateTests
{
    private static readonly IdempotencyResponseSnapshot Snapshot =
        new(201, new Dictionary<string, string>(), [1]);

    private readonly MutableTimeProvider _clock = new();
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly StubFingerprinter _fingerprinter = new("caller-digest", "request-digest");

    [Fact]
    public async Task BeginAsync_ClaimsTheKeyUnderTheFingerprintAndALeaseReadFromTheClock()
    {
        _store.BeginAsync(Arg.Any<IdempotencyReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyBeginResult(IdempotencyBeginOutcome.Started));

        var context = HttpContexts.Create();
        var result = await Gate().BeginAsync(context, "key");

        result.Scope.Should().Be("caller-digest");
        result.Fingerprint.Should().Be("request-digest");
        result.Begin.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        _fingerprinter.LastContext.Should().BeSameAs(context);

        await _store.Received(1).BeginAsync(
            Arg.Is<IdempotencyReservationRequest>(request =>
                request.Scope == "caller-digest"
                && request.Key == "key"
                && request.Fingerprint == "request-digest"
                && request.LeaseExpiresAt == _clock.GetUtcNow() + TimeSpan.FromSeconds(60)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReplayableUntil_IsTheRetentionMeasuredFromNow()
    {
        _clock.Advance(TimeSpan.FromDays(3));

        Gate(options => options.Retention = TimeSpan.FromHours(2)).ReplayableUntil
            .Should().Be(_clock.GetUtcNow() + TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task CompleteAsync_HandsTheCompletionStraightToTheStore()
    {
        var completion = new IdempotencyCompletionRequest(
            "scope", "key", "fingerprint", Snapshot, _clock.GetUtcNow());

        await Gate().CompleteAsync(completion);

        await _store.Received(1).CompleteAsync(completion, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseAsync_DropsOnlyTheReservationTheRequestMade()
    {
        await Gate().ReleaseAsync("scope", "key", "fingerprint");

        await _store.Received(1).ReleaseAsync("scope", "key", "fingerprint", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(404, true)]
    [InlineData(422, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void IsReplayable_FollowsTheStatusPolicy(int statusCode, bool replayable) =>
        Gate().IsReplayable(statusCode, bodyLength: 0).Should().Be(replayable);

    [Fact]
    public void IsReplayable_AResponseWithinTheStoreBound_IsStored() =>
        Gate(options => options.MaxResponseBytes = 4)
            .IsReplayable(200, bodyLength: 4).Should().BeTrue();

    [Fact]
    public void IsReplayable_AResponseLargerThanTheStoreBound_IsNotStored() =>
        Gate(options => options.MaxResponseBytes = 4)
            .IsReplayable(200, bodyLength: 5).Should().BeFalse();

    [Fact]
    public void RetryAfterSeconds_NoLeaseAtAll_IsTheConfiguredWait() =>
        Gate(options => options.InProgressRetryAfter = TimeSpan.FromSeconds(5))
            .RetryAfterSeconds(null).Should().Be(5);

    [Fact]
    public void RetryAfterSeconds_AFractionalWait_IsRoundedUp() =>
        Gate(options => options.InProgressRetryAfter = TimeSpan.FromMilliseconds(1500))
            .RetryAfterSeconds(null).Should().Be(2);

    [Fact]
    public void RetryAfterSeconds_ALeaseShorterThanTheConfiguredWait_CapsIt() =>
        Gate(options => options.InProgressRetryAfter = TimeSpan.FromSeconds(30))
            .RetryAfterSeconds(_clock.GetUtcNow() + TimeSpan.FromSeconds(4)).Should().Be(4);

    [Fact]
    public void RetryAfterSeconds_ALeaseLongerThanTheConfiguredWait_LeavesItAlone() =>
        Gate(options => options.InProgressRetryAfter = TimeSpan.FromSeconds(3))
            .RetryAfterSeconds(_clock.GetUtcNow() + TimeSpan.FromSeconds(45)).Should().Be(3);

    [Fact]
    public void RetryAfterSeconds_ALeaseThatHasAlreadyLapsed_IsFlooredAtASecond() =>
        Gate().RetryAfterSeconds(_clock.GetUtcNow() - TimeSpan.FromSeconds(10)).Should().Be(1);

    [Fact]
    public void RetryAfterSeconds_AConfiguredWaitBelowASecond_IsFlooredAtASecond() =>
        Gate(options => options.InProgressRetryAfter = TimeSpan.FromMilliseconds(200))
            .RetryAfterSeconds(null).Should().Be(1);

    [Fact]
    public void Constructor_NullStore_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyGate(
                null!, _fingerprinter, Options.Create(new BsftIdempotencyOptions()), _clock))
            .Should().Throw<ArgumentNullException>().WithParameterName("store");

    [Fact]
    public void Constructor_NullFingerprinter_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyGate(
                new InMemoryIdempotencyStore(_clock), null!, Options.Create(new BsftIdempotencyOptions()), _clock))
            .Should().Throw<ArgumentNullException>().WithParameterName("fingerprinter");

    [Fact]
    public void Constructor_NullOptions_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyGate(
                new InMemoryIdempotencyStore(_clock), _fingerprinter, null!, _clock))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");

    [Fact]
    public void Constructor_NullTimeProvider_IsRefused() =>
        FluentActions
            .Invoking(() => new IdempotencyGate(
                new InMemoryIdempotencyStore(_clock),
                _fingerprinter,
                Options.Create(new BsftIdempotencyOptions()),
                null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");

    [Fact]
    public async Task BeginAsync_NullContext_IsRefused() =>
        await FluentActions.Awaiting(() => Gate().BeginAsync(null!, "key").AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");

    [Fact]
    public async Task BeginAsync_NullKey_IsRefused() =>
        await FluentActions.Awaiting(() => Gate().BeginAsync(HttpContexts.Create(), null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("key");

    [Fact]
    public async Task CompleteAsync_NullRequest_IsRefused() =>
        await FluentActions.Awaiting(() => Gate().CompleteAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");

    private IdempotencyGate Gate(Action<BsftIdempotencyOptions>? configure = null)
    {
        var options = new BsftIdempotencyOptions();

        configure?.Invoke(options);

        return new IdempotencyGate(_store, _fingerprinter, Options.Create(options), _clock);
    }
}
