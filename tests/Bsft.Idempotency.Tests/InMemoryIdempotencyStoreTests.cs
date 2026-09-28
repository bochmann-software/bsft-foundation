using Bsft.Idempotency.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests;

public class InMemoryIdempotencyStoreTests
{
    private const string Scope = "scope";
    private const string Key = "key";
    private const string Fingerprint = "fingerprint";

    /// <summary>How many requests reach one key at the same instant in the concurrency tests.</summary>
    private const int Racers = 8;

    /// <summary>How many times the burst is repeated, so the interleaving is met rather than hoped for.</summary>
    private const int Rounds = 200;

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    private readonly MutableTimeProvider _clock = new();
    private readonly InMemoryIdempotencyStore _store;

    public InMemoryIdempotencyStoreTests() => _store = new InMemoryIdempotencyStore(_clock);

    [Fact]
    public async Task BeginAsync_AFreeKey_Starts()
    {
        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        result.Response.Should().BeNull();
        result.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task BeginAsync_AKeyAlreadyReservedByTheSameRequest_IsInProgressAndCarriesTheLease()
    {
        await _store.BeginAsync(Reservation());

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
        result.LeaseExpiresAt.Should().Be(_clock.GetUtcNow() + Lease);
    }

    [Fact]
    public async Task BeginAsync_AKeyReservedByADifferentRequest_IsAMismatch()
    {
        await _store.BeginAsync(Reservation());

        var result = await _store.BeginAsync(Reservation(fingerprint: "other"));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.FingerprintMismatch);
    }

    [Fact]
    public async Task BeginAsync_AKeyWhoseRequestFinished_ReplaysTheStoredResponse()
    {
        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(201)));

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
        result.Response!.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task BeginAsync_ACompletedKeyPresentedWithADifferentRequest_IsAMismatch()
    {
        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(201)));

        var result = await _store.BeginAsync(Reservation(fingerprint: "other"));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.FingerprintMismatch);
    }

    [Fact]
    public async Task BeginAsync_AReservationPastItsLease_IsReclaimed()
    {
        await _store.BeginAsync(Reservation());

        _clock.Advance(Lease + TimeSpan.FromSeconds(1));

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task BeginAsync_AReservationPastItsLeaseThatTheSweepHasNotSeenYet_IsReclaimedInPlace()
    {
        // The lease is already over, and the clock has not moved, so the lazy sweep does not run and
        // the abandoned record is still standing when the next request claims the key.
        await _store.BeginAsync(Reservation(lease: _clock.GetUtcNow()));

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task BeginAsync_ACompletedRecordPastItsRetention_RunsAgain()
    {
        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(201)));

        _clock.Advance(Retention + TimeSpan.FromSeconds(1));

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task BeginAsync_ARecordExactlyAtItsExpiry_HasStoppedDeciding()
    {
        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(201)));

        _clock.Advance(Retention);

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task BeginAsync_AKeyUnderAnotherScope_IsAMiss()
    {
        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(201)));

        var result = await _store.BeginAsync(Reservation(scope: "other-tenant"));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task CompleteAsync_AReservationWhoseKeyWasReclaimedByAnotherRequest_StoresNothing()
    {
        await _store.BeginAsync(Reservation());

        _clock.Advance(Lease + TimeSpan.FromSeconds(1));

        // The key is reclaimed by a request of its own, which is still running.
        await _store.BeginAsync(Reservation(fingerprint: "newcomer"));

        // The abandoned request finally finishes and tries to store its answer over the newcomer's.
        await _store.CompleteAsync(Completion(Snapshot(201)));

        var newcomer = await _store.BeginAsync(Reservation(fingerprint: "newcomer"));

        newcomer.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
    }

    [Fact]
    public async Task CompleteAsync_AReservationTheNewOwnerAlreadyFinished_IsDropped()
    {
        await _store.BeginAsync(Reservation());

        _clock.Advance(Lease + TimeSpan.FromSeconds(1));

        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(202)));
        await _store.CompleteAsync(Completion(Snapshot(201)));

        var result = await _store.BeginAsync(Reservation());

        result.Response!.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task CompleteAsync_AKeyNobodyReserved_StoresNothing()
    {
        await _store.CompleteAsync(Completion(Snapshot(201)));

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task ReleaseAsync_AStandingReservation_FreesTheKeyImmediately()
    {
        await _store.BeginAsync(Reservation());
        await _store.ReleaseAsync(Scope, Key, Fingerprint);

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task ReleaseAsync_AReservationThatIsNotItsOwn_LeavesItStanding()
    {
        await _store.BeginAsync(Reservation());
        await _store.ReleaseAsync(Scope, Key, "somebody-elses-request");

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
    }

    [Fact]
    public async Task ReleaseAsync_ACompletedRecord_LeavesItReplayable()
    {
        await _store.BeginAsync(Reservation());
        await _store.CompleteAsync(Completion(Snapshot(201)));

        await _store.ReleaseAsync(Scope, Key, Fingerprint);

        var result = await _store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
    }

    [Fact]
    public async Task ReleaseAsync_AKeyNobodyReserved_DoesNothing()
    {
        await _store.ReleaseAsync(Scope, "unknown", Fingerprint);

        var result = await _store.BeginAsync(Reservation(key: "unknown"));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task BeginAsync_OnceTheSweepIntervalHasPassed_ExpiredRecordsAreDroppedAndTheLiveOneIsKept()
    {
        await _store.BeginAsync(Reservation(key: "expiring"));
        await _store.CompleteAsync(Completion(Snapshot(201), key: "expiring"));
        await _store.BeginAsync(Reservation(key: "keeper"));
        await _store.CompleteAsync(
            Completion(Snapshot(204), key: "keeper", expiresAt: _clock.GetUtcNow() + TimeSpan.FromDays(1)));

        _clock.Advance(Retention + TimeSpan.FromSeconds(1));

        // The sweep runs on this call, because more than the sweep interval has passed.
        await _store.BeginAsync(Reservation(key: "trigger"));

        var expired = await _store.BeginAsync(Reservation(key: "expiring"));
        var kept = await _store.BeginAsync(Reservation(key: "keeper"));

        expired.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        kept.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
    }

    [Fact]
    public async Task BeginAsync_WithinTheSweepInterval_DoesNotSweepAgain()
    {
        await _store.BeginAsync(Reservation(key: "first"));

        _clock.Advance(TimeSpan.FromSeconds(1));

        var result = await _store.BeginAsync(Reservation(key: "second"));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task BeginAsync_BurstsOfConcurrentRequestsUnderOneFreeKey_LetExactlyOneThrough()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var key = "free-" + round.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var results = await BurstAsync(() => Reservation(key: key));

            results.Count(result => result.Outcome == IdempotencyBeginOutcome.Started).Should().Be(1);
        }
    }

    [Fact]
    public async Task BeginAsync_BurstsOfConcurrentRequestsReclaimingOneLapsedLease_LetExactlyOneThrough()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var key = "lapsed-" + round.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // A lease that is already over, so the record is abandoned without the clock moving —
            // which keeps the lazy sweep from removing it first and leaves the reclaim to the
            // compare-and-swap the concurrent requests then race on.
            await _store.BeginAsync(Reservation(key: key, lease: _clock.GetUtcNow()));

            var results = await BurstAsync(() => Reservation(key: key));

            results.Count(result => result.Outcome == IdempotencyBeginOutcome.Started).Should().Be(1);
        }
    }

    [Fact]
    public void Constructor_NullTimeProvider_IsRefused() =>
        FluentActions.Invoking(() => new InMemoryIdempotencyStore(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");

    [Fact]
    public async Task BeginAsync_NullRequest_IsRefused() =>
        await FluentActions.Awaiting(() => _store.BeginAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");

    [Fact]
    public async Task CompleteAsync_NullRequest_IsRefused() =>
        await FluentActions.Awaiting(() => _store.CompleteAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");

    [Fact]
    public async Task ReleaseAsync_NullScope_IsRefused() =>
        await FluentActions.Awaiting(() => _store.ReleaseAsync(null!, Key, Fingerprint))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("scope");

    [Fact]
    public async Task ReleaseAsync_NullKey_IsRefused() =>
        await FluentActions.Awaiting(() => _store.ReleaseAsync(Scope, null!, Fingerprint))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("key");

    [Fact]
    public async Task ReleaseAsync_NullFingerprint_IsRefused() =>
        await FluentActions.Awaiting(() => _store.ReleaseAsync(Scope, Key, null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("fingerprint");

    /// <summary>
    /// Starts a handful of requests at the same instant and returns what each of them was told.
    /// </summary>
    /// <param name="reservation">Builds the reservation each of them asks for.</param>
    /// <returns>What the store answered, one per request.</returns>
    /// <remarks>
    /// A barrier and dedicated threads rather than <c>Task.Run</c>, because the contract under test
    /// is what happens when two requests reach the same key between one thread's read and its write:
    /// work queued on the pool runs one after another often enough to never get there.
    /// </remarks>
    private async Task<IdempotencyBeginResult[]> BurstAsync(Func<IdempotencyReservationRequest> reservation)
    {
        using var barrier = new Barrier(Racers);

        var tasks = Enumerable
            .Range(0, Racers)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();

                    return _store.BeginAsync(reservation()).GetAwaiter().GetResult();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));

        return await Task.WhenAll(tasks);
    }

    private IdempotencyReservationRequest Reservation(
        string scope = Scope,
        string key = Key,
        string fingerprint = Fingerprint,
        DateTimeOffset? lease = null) =>
        new(scope, key, fingerprint, lease ?? _clock.GetUtcNow() + Lease);

    private IdempotencyCompletionRequest Completion(
        IdempotencyResponseSnapshot response,
        string scope = Scope,
        string key = Key,
        string fingerprint = Fingerprint,
        DateTimeOffset? expiresAt = null) =>
        new(scope, key, fingerprint, response, expiresAt ?? _clock.GetUtcNow() + Retention);

    private static IdempotencyResponseSnapshot Snapshot(int statusCode) =>
        new(statusCode, new Dictionary<string, string> { ["Location"] = "/orders/1" }, [1, 2, 3]);
}
