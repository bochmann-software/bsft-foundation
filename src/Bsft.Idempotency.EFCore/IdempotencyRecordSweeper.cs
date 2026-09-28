using Bsft.Core.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Idempotency.EFCore;

/// <summary>
/// Background service that keeps the idempotency table from growing for ever: it deletes the records
/// that have stopped deciding anything.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else prunes the table and it takes a row per protected request, so without this the
/// replay protection becomes the busiest table in the application.
/// </para>
/// <para>
/// The sweep is one predicate — the record's own expiry against now — and that is what makes it
/// safe. A reservation still inside its lease has an expiry in the future and is therefore never
/// deleted, so a sweep cannot free a key a request is still using; a completed record past its
/// retention decides nothing, because the retention is the promise that the key runs again.
/// </para>
/// </remarks>
internal sealed class IdempotencyRecordSweeper : BackgroundService, IPollReporter
{
    /// <summary>
    /// The longest a sweep waits for the next one, however long the retention is. A row therefore
    /// outlives its retention by at most an hour, and a retention of a day does not mean a single
    /// enormous delete every day.
    /// </summary>
    private static readonly TimeSpan MaxSweepInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// How many records one statement deletes. The table takes a row per protected request from every
    /// replica, so a first sweep after a deployment that ran without the pruner - or after the pruner
    /// was down - can meet a backlog of any size, and an unbounded delete against a table that is
    /// simultaneously taking an insert for every live request holds locks for as long as it runs. A
    /// pass that fills its batch asks to run again straight away, so the backlog is still cleared in
    /// one sitting rather than one batch per interval.
    /// </summary>
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BsftIdempotencyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdempotencyRecordSweeper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyRecordSweeper"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a scope per sweep.</param>
    /// <param name="options">The options carrying the retention that sets the cadence.</param>
    /// <param name="timeProvider">The clock expiry is judged against.</param>
    /// <param name="logger">Logger used to report what was pruned.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public IdempotencyRecordSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<BsftIdempotencyOptions> options,
        TimeProvider timeProvider,
        ILogger<IdempotencyRecordSweeper> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the prune loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal async Task RunLoopAsync(CancellationToken stoppingToken = default)
    {
        var retention = _options.Retention;

        // Sweeping at the retention keeps a short one honest without a second option to configure,
        // and the cap keeps a long one from becoming a rare, enormous delete.
        var interval = retention < MaxSweepInterval ? retention : MaxSweepInterval;

        await new PollLoop(SweepAsync, this, interval).RunAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>One pass, which asks for another when it filled its batch.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>Whether to poll again or wait for the next interval.</returns>
    private async Task<PollPassOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        // Decided on what the pass found rather than on what it deleted. Another replica sweeping the
        // same table can have deleted some of the page between the two statements, and a pass that
        // read a full page has seen that the backlog goes further whatever its own delete reports.
        var pass = await PurgePassAsync(cancellationToken).ConfigureAwait(false);

        return pass.Found == BatchSize ? PollPassOutcome.PollAgain : PollPassOutcome.WaitForInterval;
    }

    /// <summary>Deletes up to one batch of the records whose expiry has passed.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of records deleted.</returns>
    internal async Task<int> PurgeOnceAsync(CancellationToken cancellationToken = default)
        => (await PurgePassAsync(cancellationToken).ConfigureAwait(false)).Deleted;

    /// <summary>One pass over the expired records.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>How many expired records the pass found, and how many of them it deleted.</returns>
    private async Task<(int Found, int Deleted)> PurgePassAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdempotencyDbContext>();

        // Set-based rather than loading the rows, and bounded rather than open-ended. The cap costs
        // the second statement: EF Core has no portable way to bound a DELETE at N rows, and
        // Where(...).OrderBy(...).Take(n).ExecuteDeleteAsync() throws at runtime on Npgsql, which has
        // no LIMIT-on-DELETE translation. Reading the bounded page of ids and deleting exactly those
        // is the one shape every provider translates alike, and is what the audit pruners do for the
        // same reason. The two statements are not atomic with each other, so the delete carries the
        // expiry predicate a second time: unlike an audit row, an idempotency record can come back to
        // life between them - a request reclaiming a lapsed key writes a lease in the future onto the
        // same row - and deleting it by id alone would free a key a request is using. Carrying the
        // predicate means the worst a concurrent sweep leaves is a row already gone, which the delete
        // reports as zero rows.
        var expired = await db.IdempotencyRecords
            .Where(entity => entity.ExpiresAt < now)
            .OrderBy(entity => entity.Id)
            .Take(BatchSize)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pruned = expired.Count == 0
            ? 0
            : await db.IdempotencyRecords
                .Where(entity => expired.Contains(entity.Id) && entity.ExpiresAt < now)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

        if (pruned > 0)
            _logger.LogInformation(
                "Purged {PurgedCount} idempotency records that expired before {Cutoff:o}.", pruned, now);

        return (expired.Count, pruned);
    }

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure) =>
        _logger.LogError(failure, "The idempotency record purge sweep failed.");

    /// <inheritdoc />
    void IPollReporter.ReportPassCanceledWithoutStop() =>
        _logger.LogError(
            "The idempotency record purge sweep was canceled although the sweeper was not asked to stop.");

    /// <inheritdoc />
    void IPollReporter.ReportStopping() => _logger.LogDebug("Idempotency record sweeper stopping.");
}
