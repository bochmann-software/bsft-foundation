using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Idempotency.EFCore.Tests.Fakes;
using Bsft.Idempotency.EFCore.Tests.Support;

namespace Bsft.Idempotency.EFCore.Tests;

public sealed class EFCoreIdempotencyStoreTests : IDisposable
{
    private const string Scope = "scope_acme";
    private const string Key = "key_2f1c9a";
    private const string Fingerprint = "fp_first";
    private const string OtherFingerprint = "fp_other";

    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly SqliteFixture _sqlite = new();
    private readonly MutableTimeProvider _time = new(Now);
    private readonly List<TestIdempotencyDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();

        _sqlite.Dispose();
    }

    private TestIdempotencyDbContext Context(params IInterceptor[] interceptors)
    {
        var context = _sqlite.NewContextWith(interceptors);
        _contexts.Add(context);

        return context;
    }

    private EFCoreIdempotencyStore Store(
        IIdempotencyDbContext db, IdempotencyMode mode = IdempotencyMode.Detached) =>
        new(db, Options.Create(new BsftIdempotencyOptions { Mode = mode }), _time);

    private EFCoreIdempotencyStore Store(IdempotencyMode mode = IdempotencyMode.Detached) =>
        Store(Context(), mode);

    private IdempotencyReservationRequest Reservation(string fingerprint = Fingerprint, string key = Key) =>
        new(Scope, key, fingerprint, _time.GetUtcNow() + Lease);

    private IdempotencyCompletionRequest Completion(
        string fingerprint = Fingerprint,
        int statusCode = 201,
        string body = "{\"id\":1}",
        string key = Key) =>
        new(
            Scope,
            key,
            fingerprint,
            new IdempotencyResponseSnapshot(
                statusCode,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Location"] = "/orders/1",
                    ["Content-Type"] = "application/json",
                },
                Encoding.UTF8.GetBytes(body)),
            _time.GetUtcNow() + Retention);

    private async Task<List<IdempotencyRecordEntity>> ReadRecordsAsync()
    {
        await using var context = _sqlite.NewContext();

        return await context.IdempotencyRecords.AsNoTracking().OrderBy(entity => entity.Id).ToListAsync();
    }

    private async Task<IdempotencyRecordEntity> ReadRecordAsync() =>
        (await ReadRecordsAsync()).Should().ContainSingle().Subject;

    /// <summary>Writes a record through a second connection, which is what the competing replica is.</summary>
    private async Task WriteRecordAsync(
        IdempotencyRecordState state,
        DateTimeOffset expiresAt,
        string fingerprint = Fingerprint,
        int? statusCode = null,
        string? headers = null,
        byte[]? body = null,
        string key = Key)
    {
        await using var context = _sqlite.NewContext();

        context.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            Scope = Scope,
            Key = key,
            Fingerprint = fingerprint,
            State = state,
            ExpiresAt = expiresAt.UtcDateTime,
            StatusCode = statusCode,
            ResponseHeaders = headers,
            ResponseBody = body,
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_free_key_is_reserved_and_the_request_may_run()
    {
        var result = await Store().BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        result.Response.Should().BeNull();
        result.LeaseExpiresAt.Should().BeNull();

        var record = await ReadRecordAsync();
        record.Scope.Should().Be(Scope);
        record.Key.Should().Be(Key);
        record.Fingerprint.Should().Be(Fingerprint);
        record.State.Should().Be(IdempotencyRecordState.Reserved);
        record.ExpiresAt.Should().Be((Now + Lease).UtcDateTime);
        record.StatusCode.Should().BeNull();
        record.ResponseHeaders.Should().BeNull();
        record.ResponseBody.Should().BeNull();
    }

    [Fact]
    public async Task Two_different_keys_are_both_reserved()
    {
        var store = Store();

        var first = await store.BeginAsync(Reservation(key: "key_a"));
        var second = await store.BeginAsync(Reservation(key: "key_b"));

        first.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        second.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        // Each reservation is its own row, keyed by a surrogate the database assigns in insert order.
        var records = await ReadRecordsAsync();
        records.Select(record => record.Key).Should().Equal("key_a", "key_b");
        records[0].Id.Should().BePositive();
        records[1].Id.Should().BeGreaterThan(records[0].Id);
    }

    [Fact]
    public async Task A_completed_record_answers_the_same_request_with_the_stored_response()
    {
        // The whole point of the table: the second request never runs, and what it is answered with is
        // byte-identical to what the first produced, headers included.
        var store = Store();
        await store.BeginAsync(Reservation());
        await store.CompleteAsync(Completion());

        var replay = await Store().BeginAsync(Reservation());

        replay.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
        replay.Response!.StatusCode.Should().Be(201);
        replay.Response.Headers.Should().Contain(new KeyValuePair<string, string>("Location", "/orders/1"));
        replay.Response.Headers.Should()
            .Contain(new KeyValuePair<string, string>("Content-Type", "application/json"));
        Encoding.UTF8.GetString(replay.Response.Body).Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task The_headers_are_stored_as_json_on_the_record()
    {
        var store = Store();
        await store.BeginAsync(Reservation());

        await store.CompleteAsync(Completion());

        var record = await ReadRecordAsync();
        record.State.Should().Be(IdempotencyRecordState.Completed);
        record.ExpiresAt.Should().Be((Now + Retention).UtcDateTime);
        record.StatusCode.Should().Be(201);
        JsonSerializer.Deserialize<Dictionary<string, string>>(record.ResponseHeaders!)
            .Should().ContainKey("Location");
        Encoding.UTF8.GetString(record.ResponseBody!).Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task A_completed_record_whose_columns_were_never_written_replays_a_usable_response()
    {
        // A row edited by hand, or written by an older version. Replaying a status of zero is something
        // no client can read, so the fallbacks stand in for what is missing rather than crashing.
        await WriteRecordAsync(IdempotencyRecordState.Completed, Now + Retention);

        var replay = await Store().BeginAsync(Reservation());

        replay.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
        replay.Response!.StatusCode.Should().Be(200);
        replay.Response.Headers.Should().BeEmpty();
        replay.Response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task A_completed_record_whose_headers_are_the_json_null_literal_replays_no_headers()
    {
        // The column is present but says nothing, which deserializes to null rather than to a
        // dictionary; the replay must still get a dictionary it can enumerate.
        await WriteRecordAsync(
            IdempotencyRecordState.Completed, Now + Retention, statusCode: 204, headers: "null");

        var replay = await Store().BeginAsync(Reservation());

        replay.Response!.StatusCode.Should().Be(204);
        replay.Response.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_presented_again_with_a_different_request_is_refused_while_it_is_reserved()
    {
        // Serving the stored answer would answer a question the caller did not ask, and letting it run
        // would defeat the key it presented.
        await Store().BeginAsync(Reservation());

        var result = await Store().BeginAsync(Reservation(OtherFingerprint));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.FingerprintMismatch);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task A_key_presented_again_with_a_different_request_is_refused_once_it_is_completed()
    {
        var store = Store();
        await store.BeginAsync(Reservation());
        await store.CompleteAsync(Completion());

        var result = await Store().BeginAsync(Reservation(OtherFingerprint));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.FingerprintMismatch);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task A_reservation_still_inside_its_lease_tells_the_caller_when_to_come_back()
    {
        // Told to come back rather than blocked: blocking would hold a connection for the duration of
        // somebody else's work, and the lease is what bounds the Retry-After.
        await Store().BeginAsync(Reservation());

        var result = await Store().BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
        result.LeaseExpiresAt.Should().Be(Now + Lease);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task A_reservation_exactly_at_its_lease_is_already_reclaimable()
    {
        // The comparison is strict, so the boundary belongs to the next request: the lease is how long
        // the reservation stands, and on reaching it the key is retryable.
        await Store().BeginAsync(Reservation());

        _time.Advance(Lease);

        (await Store().BeginAsync(Reservation())).Outcome
            .Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task A_reservation_whose_lease_lapsed_is_reclaimed_on_its_own_row()
    {
        // This is what makes a crash between reservation and completion recoverable without an
        // operator: the lease lapses and the next request under that key simply runs.
        await Store().BeginAsync(Reservation());

        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var result = await Store().BeginAsync(Reservation(OtherFingerprint));

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        var record = await ReadRecordAsync();
        record.Fingerprint.Should().Be(OtherFingerprint);
        record.State.Should().Be(IdempotencyRecordState.Reserved);
        record.ExpiresAt.Should().Be((_time.GetUtcNow() + Lease).UtcDateTime);
    }

    [Fact]
    public async Task A_completed_record_past_its_retention_is_reclaimed_and_its_response_cleared()
    {
        // The retention is a contract: after it the same key runs again. A stale response left in the
        // columns would be replayed by the next completion's reader if the request died in between.
        var store = Store();
        await store.BeginAsync(Reservation());
        await store.CompleteAsync(Completion());

        _time.Advance(Retention + TimeSpan.FromSeconds(1));
        var result = await Store().BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        var record = await ReadRecordAsync();
        record.State.Should().Be(IdempotencyRecordState.Reserved);
        record.StatusCode.Should().BeNull();
        record.ResponseHeaders.Should().BeNull();
        record.ResponseBody.Should().BeNull();
    }

    [Fact]
    public async Task The_loser_of_the_insert_race_is_told_the_first_request_is_still_running()
    {
        // Two replicas taking the same retry: both read nothing, so the read settles nothing and the
        // unique index has to. The refusal is read back as the standing record rather than surfaced as
        // an error, because an error here is a duplicate the client will simply send again.
        var racing = Context(new CommitConflictOnFirstSave(() =>
            WriteRecordAsync(IdempotencyRecordState.Reserved, Now + Lease)));

        var result = await Store(racing).BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
        result.LeaseExpiresAt.Should().Be(Now + Lease);
        (await ReadRecordsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task The_loser_of_the_insert_race_replays_a_response_that_landed_first()
    {
        // The same race, one step later: the winner had already finished, so the loser is answered
        // rather than told to wait.
        var racing = Context(new CommitConflictOnFirstSave(() => WriteRecordAsync(
            IdempotencyRecordState.Completed,
            Now + Retention,
            statusCode: 201,
            headers: "{\"Location\":\"/orders/1\"}",
            body: Encoding.UTF8.GetBytes("{\"id\":1}"))));

        var result = await Store(racing).BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Replayed);
        result.Response!.StatusCode.Should().Be(201);
        result.Response.Headers["Location"].Should().Be("/orders/1");
    }

    [Fact]
    public async Task The_loser_of_the_insert_race_is_refused_when_the_winner_sent_another_request()
    {
        var racing = Context(new CommitConflictOnFirstSave(() =>
            WriteRecordAsync(IdempotencyRecordState.Reserved, Now + Lease, OtherFingerprint)));

        var result = await Store(racing).BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.FingerprintMismatch);
    }

    [Fact]
    public async Task A_save_that_failed_for_another_reason_is_not_read_as_a_standing_record()
    {
        // An unreachable database throws the same exception a unique violation does. Reading it as a
        // race would let the request run unprotected, so a save that left no row behind is surfaced.
        var failing = Context(new FailEverySave());

        var act = async () => await Store(failing).BeginAsync(Reservation());

        await act.Should().ThrowAsync<DbUpdateException>();
        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_loser_of_the_race_for_a_lapsed_reservation_is_told_to_come_back()
    {
        // The other half of the race, on the row a lapsed reservation leaves behind. The expiry is in
        // the update's own predicate, so the request that arrives second updates nothing and reads the
        // reservation the winner now holds instead of both being allowed to run.
        await WriteRecordAsync(IdempotencyRecordState.Reserved, Now - TimeSpan.FromMinutes(1));

        var racing = Context(new RunBeforeTheUpdate(ReclaimThroughAnotherConnectionAsync));

        var result = await Store(racing).BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.InProgress);
        result.LeaseExpiresAt.Should().Be(Now + Lease);
        (await ReadRecordsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_record_the_pruner_deleted_mid_reclaim_is_reserved_afresh()
    {
        // A sweep and a reclaim genuinely compete for a lapsed row. An update that took nothing because
        // the row is gone must not be read as a standing record: there is none, and the request is the
        // first under that key.
        await WriteRecordAsync(IdempotencyRecordState.Reserved, Now - TimeSpan.FromMinutes(1));

        var racing = Context(new RunBeforeTheUpdate(PruneThroughAnotherConnectionAsync));

        var result = await Store(racing).BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);

        var record = await ReadRecordAsync();
        record.State.Should().Be(IdempotencyRecordState.Reserved);
        record.ExpiresAt.Should().Be((Now + Lease).UtcDateTime);
    }

    [Fact]
    public async Task A_completion_from_a_request_that_lost_its_key_leaves_the_new_reservation_alone()
    {
        // The state alone is not proof that the row is this request's own. A request whose lease lapsed
        // while it was still running comes back to a key somebody else now holds, and writing its
        // response there would store an answer under a fingerprint that never asked for it — and would
        // leave the request that does hold the key unable to store its own.
        var slow = Store();
        await slow.BeginAsync(Reservation());

        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var fast = Store();
        await fast.BeginAsync(Reservation(OtherFingerprint));

        await slow.CompleteAsync(Completion(statusCode: 201, body: "{\"id\":1}"));

        var untouched = await ReadRecordAsync();
        untouched.Fingerprint.Should().Be(OtherFingerprint);
        untouched.State.Should().Be(IdempotencyRecordState.Reserved);
        untouched.StatusCode.Should().BeNull();
        untouched.ResponseBody.Should().BeNull();
    }

    [Fact]
    public async Task The_request_that_holds_the_key_can_still_complete_after_the_one_that_lost_it_tried()
    {
        // The other half of the same statement: refusing the stale completion must not have cost the
        // rightful holder its own, which is the reservation it is still running against.
        var slow = Store();
        await slow.BeginAsync(Reservation());

        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var fast = Store();
        await fast.BeginAsync(Reservation(OtherFingerprint));
        await slow.CompleteAsync(Completion(statusCode: 201, body: "{\"id\":1}"));

        await fast.CompleteAsync(Completion(OtherFingerprint, statusCode: 202, body: "{\"id\":2}"));

        var record = await ReadRecordAsync();
        record.Fingerprint.Should().Be(OtherFingerprint);
        record.State.Should().Be(IdempotencyRecordState.Completed);
        record.StatusCode.Should().Be(202);
        Encoding.UTF8.GetString(record.ResponseBody!).Should().Be("{\"id\":2}");
    }

    [Fact]
    public async Task A_release_from_a_request_that_lost_its_key_leaves_the_new_reservation_alone()
    {
        // The same hazard on the failure path, and the worse one: a stale release would free a key
        // another request is actively running under, and the retry it lets in would run the work twice.
        var slow = Store();
        await slow.BeginAsync(Reservation());

        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var fast = Store();
        await fast.BeginAsync(Reservation(OtherFingerprint));

        await slow.ReleaseAsync(Scope, Key, Fingerprint);

        var untouched = await ReadRecordAsync();
        untouched.Fingerprint.Should().Be(OtherFingerprint);
        untouched.State.Should().Be(IdempotencyRecordState.Reserved);
    }

    [Fact]
    public async Task The_request_that_holds_the_key_can_still_complete_after_the_one_that_lost_it_released()
    {
        var slow = Store();
        await slow.BeginAsync(Reservation());

        _time.Advance(Lease + TimeSpan.FromSeconds(1));
        var fast = Store();
        await fast.BeginAsync(Reservation(OtherFingerprint));
        await slow.ReleaseAsync(Scope, Key, Fingerprint);

        await fast.CompleteAsync(Completion(OtherFingerprint, statusCode: 202, body: "{\"id\":2}"));

        var record = await ReadRecordAsync();
        record.State.Should().Be(IdempotencyRecordState.Completed);
        record.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task A_second_completion_of_the_same_record_does_not_overwrite_the_stored_answer()
    {
        // The state half of the guard, with the fingerprint matching: the record is settled, and a
        // completion arriving twice must not move the retention window or the answer under it.
        var store = Store();
        await store.BeginAsync(Reservation());
        await store.CompleteAsync(Completion(statusCode: 201, body: "{\"id\":1}"));

        await store.CompleteAsync(Completion(statusCode: 500, body: "boom"));

        var record = await ReadRecordAsync();
        record.StatusCode.Should().Be(201);
        Encoding.UTF8.GetString(record.ResponseBody!).Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task A_completion_for_a_key_that_is_not_reserved_writes_nothing()
    {
        await Store().CompleteAsync(Completion());

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_release_frees_the_key_for_an_immediate_retry()
    {
        // What a server error does. The client asked to be allowed to retry, and a record that outlived
        // the failure would refuse exactly that.
        var store = Store();
        await store.BeginAsync(Reservation());

        await store.ReleaseAsync(Scope, Key, Fingerprint);

        (await ReadRecordsAsync()).Should().BeEmpty();
        (await Store().BeginAsync(Reservation())).Outcome
            .Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task A_release_never_deletes_a_completed_record()
    {
        // A release arriving after the record was completed — by this request's own late error path —
        // would throw away the answer a replay is owed.
        var store = Store();
        await store.BeginAsync(Reservation());
        await store.CompleteAsync(Completion());

        await Store().ReleaseAsync(Scope, Key, Fingerprint);

        (await ReadRecordAsync()).State.Should().Be(IdempotencyRecordState.Completed);
    }

    [Fact]
    public async Task An_enlisted_reservation_is_only_staged_until_the_caller_saves()
    {
        // The guarantee the mode exists for: the reservation cannot exist without the row it protects,
        // because until the endpoint's own SaveChanges neither of them exists at all.
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);

        var result = await store.BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_enlisted_handler_commits_the_business_row_and_the_reservation_together()
    {
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        context.BusinessRecords.Add(new BusinessRecordEntity { Name = "order" });
        await store.CompleteAsync(Completion());
        await context.SaveChangesAsync();

        var record = await ReadRecordAsync();
        record.State.Should().Be(IdempotencyRecordState.Completed);
        record.StatusCode.Should().Be(201);
        record.ExpiresAt.Should().Be((Now + Retention).UtcDateTime);
        Encoding.UTF8.GetString(record.ResponseBody!).Should().Be("{\"id\":1}");

        await using var reader = _sqlite.NewContext();
        (await reader.BusinessRecords.AsNoTracking().ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task An_enlisted_completion_writes_nothing_before_the_caller_saves()
    {
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        await store.CompleteAsync(Completion());

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_enlisted_completion_after_the_handler_already_saved_is_still_written()
    {
        // The order the middleware actually uses: the endpoint runs — and, under the CRUD
        // conventions, saves — before the response exists to be stored. That save commits the staged
        // reservation too, so by the time the completion arrives the row is an ordinary committed
        // row. Mutating the stale staged reference and returning would write nothing at all and
        // leave the record Reserved for the whole lease, with no replay ever served.
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        context.BusinessRecords.Add(new BusinessRecordEntity { Name = "order" });
        await context.SaveChangesAsync();

        await store.CompleteAsync(Completion());

        var record = await ReadRecordAsync();
        record.State.Should().Be(IdempotencyRecordState.Completed);
        record.StatusCode.Should().Be(201);
        Encoding.UTF8.GetString(record.ResponseBody!).Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task An_enlisted_release_after_the_handler_already_saved_still_frees_the_key()
    {
        // The same order, on the failure path: detaching a reservation the endpoint's own save has
        // already committed would leave an orphaned Reserved row blocking the key for the whole
        // lease, which is exactly the retry the header exists to permit.
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        await context.SaveChangesAsync();

        await store.ReleaseAsync(Scope, Key, Fingerprint);

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_completed_enlisted_claim_is_no_longer_the_one_a_later_release_drops()
    {
        // The staged claim belongs to the operation that staged it. Held past its completion it would
        // still be what a later release detaches, so a release meant for another key would silently
        // become "un-commit the record I just completed" and leave the intended row standing.
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());
        await store.CompleteAsync(Completion());
        await context.SaveChangesAsync();

        await WriteRecordAsync(IdempotencyRecordState.Reserved, Now + Lease, key: "key_other");
        await store.ReleaseAsync(Scope, "key_other", Fingerprint);

        var records = await ReadRecordsAsync();
        records.Should().ContainSingle();
        records[0].Key.Should().Be(Key);
        records[0].State.Should().Be(IdempotencyRecordState.Completed);
    }

    [Fact]
    public async Task A_detached_reservation_stands_although_the_handler_never_saved()
    {
        // The mode contrast, and the honest limit of the default: a handler that never commits leaves a
        // reserved key with no business row behind it, retryable only once the lease lapses.
        var context = Context();
        var store = Store(context);

        await store.BeginAsync(Reservation());
        context.BusinessRecords.Add(new BusinessRecordEntity { Name = "order" });

        (await ReadRecordAsync()).State.Should().Be(IdempotencyRecordState.Reserved);

        await using var reader = _sqlite.NewContext();
        (await reader.BusinessRecords.AsNoTracking().ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_enlisted_release_leaves_nothing_behind_when_the_caller_saves()
    {
        // Never committed, so there is nothing to delete: dropping it from the change tracker is the
        // whole of the release, and leaving it there would commit a reservation for a failed request.
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        await store.ReleaseAsync(Scope, Key, Fingerprint);
        await context.SaveChangesAsync();

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_enlisted_release_frees_the_key_for_the_next_request()
    {
        var context = Context();
        var store = Store(context, IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());
        await store.ReleaseAsync(Scope, Key, Fingerprint);
        await context.SaveChangesAsync();

        var again = await Store(IdempotencyMode.Enlisted).BeginAsync(Reservation());

        again.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
    }

    [Fact]
    public async Task An_enlisted_reclaim_is_written_immediately_because_it_is_crash_recovery()
    {
        // A reclaim that were only staged would leave the lapsed reservation standing for as long as the
        // handler ran, so the write is immediate whatever the mode.
        await WriteRecordAsync(IdempotencyRecordState.Reserved, Now - TimeSpan.FromMinutes(1));
        var context = Context();

        var result = await Store(context, IdempotencyMode.Enlisted).BeginAsync(Reservation());

        result.Outcome.Should().Be(IdempotencyBeginOutcome.Started);
        (await ReadRecordAsync()).ExpiresAt.Should().Be((Now + Lease).UtcDateTime);
    }

    [Fact]
    public async Task An_enlisted_completion_of_a_reclaimed_record_writes_through()
    {
        // There is no staged claim behind a reclaim, so the completion takes the guarded write and must
        // still land without the caller saving anything.
        await WriteRecordAsync(IdempotencyRecordState.Reserved, Now - TimeSpan.FromMinutes(1));
        var store = Store(IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        await store.CompleteAsync(Completion());

        (await ReadRecordAsync()).State.Should().Be(IdempotencyRecordState.Completed);
    }

    [Fact]
    public async Task An_enlisted_release_of_a_reclaimed_record_deletes_it()
    {
        await WriteRecordAsync(IdempotencyRecordState.Reserved, Now - TimeSpan.FromMinutes(1));
        var store = Store(IdempotencyMode.Enlisted);
        await store.BeginAsync(Reservation());

        await store.ReleaseAsync(Scope, Key, Fingerprint);

        (await ReadRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_reservation_request_is_rejected()
    {
        var act = async () => await Store().BeginAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task A_missing_completion_request_is_rejected()
    {
        var act = async () => await Store().CompleteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task A_missing_scope_is_rejected_by_the_release()
    {
        var act = async () => await Store().ReleaseAsync(null!, Key, Fingerprint);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("scope");
    }

    [Fact]
    public async Task A_missing_key_is_rejected_by_the_release()
    {
        var act = async () => await Store().ReleaseAsync(Scope, null!, Fingerprint);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("key");
    }

    [Fact]
    public async Task A_missing_fingerprint_is_rejected_by_the_release()
    {
        // It is half of what decides whether the row is this request's own, so a release without one
        // must not fall through to a delete guarded on the state alone.
        var act = async () => await Store().ReleaseAsync(Scope, Key, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("fingerprint");
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        var db = Context();
        var options = Options.Create(new BsftIdempotencyOptions());

        var withoutDb = () => new EFCoreIdempotencyStore(null!, options, _time);
        var withoutOptions = () => new EFCoreIdempotencyStore(db, null!, _time);
        var withoutTime = () => new EFCoreIdempotencyStore(db, options, null!);

        withoutDb.Should().Throw<ArgumentNullException>().WithParameterName("db");
        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    /// <summary>Takes the lapsed reservation over through a second connection, ahead of the reclaim.</summary>
    private async Task ReclaimThroughAnotherConnectionAsync()
    {
        await using var context = _sqlite.NewContext();

        await context.IdempotencyRecords
            .Where(entity => entity.Scope == Scope && entity.Key == Key)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.State, IdempotencyRecordState.Reserved)
                .SetProperty(entity => entity.ExpiresAt, (_time.GetUtcNow() + Lease).UtcDateTime));
    }

    /// <summary>Deletes the record through a second connection, which is what a sweep landing here is.</summary>
    private async Task PruneThroughAnotherConnectionAsync()
    {
        await using var context = _sqlite.NewContext();

        await context.IdempotencyRecords
            .Where(entity => entity.Scope == Scope && entity.Key == Key)
            .ExecuteDeleteAsync();
    }
}
