using Bsft.Idempotency.Auditing;
using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests;

public class IdempotencyBeginResultTests
{
    [Fact]
    public void AStartedResult_LetsTheRequestRunAndCarriesNothingElse()
    {
        var result = new IdempotencyBeginResult(IdempotencyBeginOutcome.Started);

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        result.Response.Should().BeNull();
        result.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public void AReplayResult_CarriesTheStoredResponse()
    {
        var snapshot = new IdempotencyResponseSnapshot(201, new Dictionary<string, string>(), [7]);

        var result = new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed, snapshot);

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
        result.Response.Should().BeSameAs(snapshot);
    }

    [Fact]
    public void AnInProgressResult_CarriesTheLeaseTheRetryAfterIsBoundedBy()
    {
        var lease = DateTimeOffset.UnixEpoch.AddMinutes(5);

        var result = new IdempotencyBeginResult(IdempotencyBeginOutcome.InProgress, LeaseExpiresAt: lease);

        result.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
        result.LeaseExpiresAt.Should().Be(lease);
        result.Response.Should().BeNull();
    }

    [Fact]
    public void TheCarrierRecords_HoldWhatTheyWereGiven()
    {
        var snapshot = new IdempotencyResponseSnapshot(
            201, new Dictionary<string, string> { ["Location"] = "/orders/1" }, [1]);
        var reservation = new IdempotencyReservationRequest("scope", "key", "fp", DateTimeOffset.UnixEpoch);
        var completion = new IdempotencyCompletionRequest(
            "scope", "key", "fp", snapshot, DateTimeOffset.UnixEpoch);
        var audit = new IdempotencyAuditEvent(
            IdempotencyAuditAction.Replayed, "scope", "key", DateTimeOffset.UnixEpoch);

        reservation.Fingerprint.Should().Be("fp");
        reservation.LeaseExpiresAt.Should().Be(DateTimeOffset.UnixEpoch);
        completion.Fingerprint.Should().Be("fp");
        completion.Response.Should().BeSameAs(snapshot);
        completion.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch);
        audit.Action.Should().Be(IdempotencyAuditAction.Replayed);
        audit.Scope.Should().Be("scope");
        audit.Key.Should().Be("key");
        audit.OccurredAt.Should().Be(DateTimeOffset.UnixEpoch);
        snapshot.Headers.Should().ContainKey("Location");
    }

    [Fact]
    public void TheRecordStates_AreOnlyTheTwoThatAreEverStored() =>
        Enum.GetValues<IdempotencyRecordState>()
            .Should().Equal(IdempotencyRecordState.Reserved, IdempotencyRecordState.Completed);

    [Fact]
    public void TheModes_AreTheTwoTheDesignStates() =>
        Enum.GetValues<IdempotencyMode>().Should().Equal(IdempotencyMode.Detached, IdempotencyMode.Enlisted);
}
