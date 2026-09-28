using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Bsft.Idempotency.EFCore;

/// <summary>
/// The shared idempotency store: one table, one unique index, and the database as the arbiter of
/// every race.
/// </summary>
/// <remarks>
/// <para>
/// It replaces the in-process default rather than adding to it. The default multiplies the duplicate
/// window by the number of replicas — each has its own memory, so a retry that lands on another
/// instance runs — and a table has one answer however many processes ask.
/// </para>
/// <para>
/// The two modes differ in one line of this class and in nothing else:
/// <see cref="IdempotencyMode.Detached"/> saves the reservation as soon as it is staged, so it is
/// visible to a concurrent request immediately and survives a crash of this one;
/// <see cref="IdempotencyMode.Enlisted"/> leaves it staged, so it commits with the endpoint's own
/// <c>SaveChanges</c> and cannot exist without the row it protects. What each buys is on the topic
/// page as a table, because the difference is a guarantee rather than a preference.
/// </para>
/// <para>
/// That one line is also where the store contract's "the insert itself is the arbiter" reads
/// differently in the two modes. Under <see cref="IdempotencyMode.Detached"/> it holds as written:
/// the loser of a race is refused by the unique index here and answered with the standing record.
/// Under <see cref="IdempotencyMode.Enlisted"/> the insert is the host's to commit, so the refusal
/// arrives at the host's own <c>SaveChanges</c> as a <c>DbUpdateException</c> and takes the business
/// write with it. That is the trade the mode is, not an oversight: a reservation that cannot exist
/// without the row it protects cannot be committed before that row exists, and a duplicate that
/// fails the whole unit of work is the outcome a caller asked for when it chose atomicity. The topic
/// page states it in the same table as the guarantee it buys. A reclaim is the exception and is
/// written immediately in both modes.
/// </para>
/// </remarks>
internal sealed class EFCoreIdempotencyStore : IIdempotencyStore
{
    /// <summary>How the header allowlist is stored. Camel case is not wanted: these are header names.</summary>
    private static readonly JsonSerializerOptions HeaderJson = new();

    /// <summary>
    /// The status a completed record falls back to when its own was somehow not stored. It exists so
    /// a row edited by hand cannot make a replay write a status of zero, which no client can read.
    /// </summary>
    private const int FallbackStatusCode = 200;

    private readonly IIdempotencyDbContext _db;
    private readonly BsftIdempotencyOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>The reservation this request staged but has not committed, in enlisted mode.</summary>
    private IdempotencyRecordEntity? _stagedClaim;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreIdempotencyStore"/> class.
    /// </summary>
    /// <param name="db">The application's context, carrying the record set.</param>
    /// <param name="options">The mode, and the windows the records are written with.</param>
    /// <param name="timeProvider">The clock every expiry is judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public EFCoreIdempotencyStore(
        IIdempotencyDbContext db, IOptions<BsftIdempotencyOptions> options, TimeProvider timeProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public async Task<IdempotencyBeginResult> BeginAsync(
        IdempotencyReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await FindAsync(request.Scope, request.Key, cancellationToken).ConfigureAwait(false);

        if (existing is null)
            return await InsertAsync(request, cancellationToken).ConfigureAwait(false);

        return existing.ExpiresAt > now
            ? Describe(existing, request.Fingerprint)
            : await ReclaimAsync(request, now, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public async Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = request.Response;
        var headers = JsonSerializer.Serialize(response.Headers, HeaderJson);
        var expiresAt = request.ExpiresAt.UtcDateTime;

        // Only while the reservation is still staged. The endpoint runs between the reservation and
        // this call and, under the CRUD conventions, saves before it returns — which commits the
        // staged row too and leaves it tracked as unchanged. Mutating it then and returning would
        // write nothing at all: the record would stay Reserved for the whole lease and no replay
        // would ever be served. So the shortcut is taken only while the insert is genuinely pending,
        // and every other case falls through to the write below.
        if (TakeStagedClaim() is { } staged)
        {
            staged.State = IdempotencyRecordState.Completed;
            staged.ExpiresAt = expiresAt;
            staged.StatusCode = response.StatusCode;
            staged.ResponseHeaders = headers;
            staged.ResponseBody = response.Body;

            return;
        }

        // Guarded on the state and on the fingerprint, so a reservation whose lease lapsed and was
        // reclaimed by another request is not overwritten with a response that is no longer the one
        // a replay should serve.
        await Owned(request.Scope, request.Key, request.Fingerprint)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(entity => entity.State, IdempotencyRecordState.Completed)
                    .SetProperty(entity => entity.ExpiresAt, expiresAt)
                    .SetProperty(entity => entity.StatusCode, (int?)response.StatusCode)
                    .SetProperty(entity => entity.ResponseHeaders, headers)
                    .SetProperty(entity => entity.ResponseBody, response.Body),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public async Task ReleaseAsync(
        string scope, string key, string fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fingerprint);

        if (TakeStagedClaim() is { } staged)
        {
            // Still uncommitted, so there is nothing to delete: dropping it from the change tracker
            // is the whole of the release, and leaving it there would commit a reservation for a
            // request that failed. Once the endpoint's own save has committed it, the delete below
            // is what frees the key.
            _db.Entry(staged).State = EntityState.Detached;

            return;
        }

        await Owned(scope, key, fingerprint).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands back the reservation this request staged, but only while it is genuinely still pending,
    /// and forgets it either way.
    /// </summary>
    /// <returns>The staged entity, or <see langword="null"/> when there is nothing pending.</returns>
    /// <remarks>
    /// The change-tracker state is the question, not whether the field is set: an entity that was
    /// <see cref="EntityState.Added"/> when this store staged it is <see cref="EntityState.Unchanged"/>
    /// once the endpoint's own <c>SaveChanges</c> has run, and at that point it is a committed row
    /// like any other and has to be written to, not merely mutated in memory.
    /// </remarks>
    private IdempotencyRecordEntity? TakeStagedClaim()
    {
        var staged = _stagedClaim;

        _stagedClaim = null;

        return staged is not null && _db.Entry(staged).State == EntityState.Added ? staged : null;
    }

    /// <summary>Turns a standing record into the answer the caller gets.</summary>
    /// <param name="existing">The record that is in the way.</param>
    /// <param name="fingerprint">The fingerprint of the request asking.</param>
    /// <returns>What happened.</returns>
    private static IdempotencyBeginResult Describe(IdempotencyRecordEntity existing, string fingerprint)
    {
        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.FingerprintMismatch);

        if (existing.State != IdempotencyRecordState.Completed)
        {
            return new IdempotencyBeginResult(
                IdempotencyBeginOutcome.InProgress,
                LeaseExpiresAt: new DateTimeOffset(existing.ExpiresAt, TimeSpan.Zero));
        }

        return new IdempotencyBeginResult(IdempotencyBeginOutcome.Replayed, Restore(existing));
    }

    /// <summary>Rebuilds the stored response from its columns.</summary>
    /// <param name="existing">The completed record.</param>
    /// <returns>The response a replay writes.</returns>
    private static IdempotencyResponseSnapshot Restore(IdempotencyRecordEntity existing)
    {
        var headers = existing.ResponseHeaders is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(existing.ResponseHeaders, HeaderJson);

        return new IdempotencyResponseSnapshot(
            existing.StatusCode ?? FallbackStatusCode,
            headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            existing.ResponseBody ?? []);
    }

    /// <summary>Reads the record for a key, without tracking it.</summary>
    /// <param name="scope">The caller the key belongs to.</param>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The record, or null.</returns>
    /// <remarks>
    /// Untracked because nothing is loaded to be changed: an existing record is read to be judged,
    /// and tracking it would put a row this request must not touch into the change tracker the
    /// endpoint is about to save.
    /// </remarks>
    private Task<IdempotencyRecordEntity?> FindAsync(string scope, string key, CancellationToken cancellationToken) =>
        _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Scope == scope && entity.Key == key, cancellationToken);

    /// <summary>
    /// This request's own reserved row, as the query the set-based writes are built on. It matches
    /// nothing when this request holds no reservation, which is exactly what a completion arriving
    /// after somebody else reclaimed the key should touch.
    /// </summary>
    /// <param name="scope">The caller the key belongs to.</param>
    /// <param name="key">The key.</param>
    /// <param name="fingerprint">The digest the key was reserved with.</param>
    /// <returns>The query.</returns>
    private IQueryable<IdempotencyRecordEntity> Owned(string scope, string key, string fingerprint) =>
        _db.IdempotencyRecords.Where(entity =>
            entity.Scope == scope
            && entity.Key == key
            && entity.Fingerprint == fingerprint
            && entity.State == IdempotencyRecordState.Reserved);

    /// <summary>Stages the reservation, and commits it unless the host asked to commit it itself.</summary>
    /// <param name="request">What is being reserved.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>What happened.</returns>
    private async Task<IdempotencyBeginResult> InsertAsync(
        IdempotencyReservationRequest request, CancellationToken cancellationToken)
    {
        var row = new IdempotencyRecordEntity
        {
            Scope = request.Scope,
            Key = request.Key,
            Fingerprint = request.Fingerprint,
            State = IdempotencyRecordState.Reserved,
            ExpiresAt = request.LeaseExpiresAt.UtcDateTime,
        };

        _db.IdempotencyRecords.Add(row);

        if (_options.Mode == IdempotencyMode.Enlisted)
        {
            _stagedClaim = row;

            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Started);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Started);
        }
        catch (DbUpdateException)
        {
            // The read above cannot settle a race: two concurrent requests under one key both find
            // nothing. The unique index settles it by refusing this insert, and the refusal is read
            // back as the standing record rather than surfaced as an error.
            _db.Entry(row).State = EntityState.Detached;

            var current = await FindAsync(request.Scope, request.Key, cancellationToken).ConfigureAwait(false);

            if (current is null)
                throw;

            return Describe(current, request.Fingerprint);
        }
    }

    /// <summary>Takes over a record that has stopped deciding anything.</summary>
    /// <param name="request">What is being reserved.</param>
    /// <param name="now">The instant expiry is judged against.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The expiry is in the predicate rather than checked beforehand, so the database is the arbiter
    /// of the reclaim as well as of the insert: two requests finding the same lapsed reservation
    /// cannot both take it over. The write is immediate whatever the mode, because a reclaim is
    /// crash recovery — a reservation that is only staged would leave the lapsed one standing.
    /// </remarks>
    private async Task<IdempotencyBeginResult> ReclaimAsync(
        IdempotencyReservationRequest request, DateTime now, CancellationToken cancellationToken)
    {
        var lease = request.LeaseExpiresAt.UtcDateTime;
        var fingerprint = request.Fingerprint;

        var reclaimed = await _db.IdempotencyRecords
            .Where(entity =>
                entity.Scope == request.Scope && entity.Key == request.Key && entity.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(entity => entity.Fingerprint, fingerprint)
                    .SetProperty(entity => entity.State, IdempotencyRecordState.Reserved)
                    .SetProperty(entity => entity.ExpiresAt, lease)
                    .SetProperty(entity => entity.StatusCode, (int?)null)
                    .SetProperty(entity => entity.ResponseHeaders, (string?)null)
                    .SetProperty(entity => entity.ResponseBody, (byte[]?)null),
                cancellationToken)
            .ConfigureAwait(false);

        if (reclaimed > 0)
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Started);

        // Somebody else reclaimed it, or the pruner removed it, between the read and the update.
        var current = await FindAsync(request.Scope, request.Key, cancellationToken).ConfigureAwait(false);

        return current is null
            ? await InsertAsync(request, cancellationToken).ConfigureAwait(false)
            : Describe(current, request.Fingerprint);
    }

}
