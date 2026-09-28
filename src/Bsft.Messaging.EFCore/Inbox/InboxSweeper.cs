using Bsft.Core.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.EFCore.Inbox;

/// <summary>
/// Background service that keeps the EF Core inbox from growing forever: it deletes the records of
/// deliveries this consumer completed longer ago than <see cref="ConsumerOptions.InboxRetention"/>.
/// </summary>
/// <remarks>
/// <para>
/// It does nothing at all unless a retention is set, which is why it can be registered unconditionally.
/// Nothing else prunes the table, and the in-memory ledger's documentation points here as the durable
/// inbox that is pruned deliberately rather than bounded by a capacity, so without this the "keeps its
/// record until something prunes it" was a promise with nobody behind it.
/// </para>
/// <para>
/// Practically every row in the table is a completed delivery — <see cref="Bsft.Messaging.EFCore.Inbox.EFCoreInboxStore"/> stages
/// the claim and only the commit after the handlers succeeded writes it. The exception is a delivery
/// one of whose handlers called <c>SaveChanges</c> itself and so carried the staged mark in early; that
/// row exists for as long as the rest of the delivery takes, and is deleted again if the delivery then
/// fails. A sweep can only reach it once it is older than the retention, which is orders of magnitude
/// longer than a delivery lasts, so there is still no status to check beyond the age. What the age does
/// decide is
/// the duplicate-detection window: a row is the only thing that refuses a redelivery of its event, so
/// deleting it at <see cref="ConsumerOptions.InboxRetention"/> means a redelivery arriving after that is
/// handled a second time. That is the trade the option documents, and it is why the retention is not
/// defaulted to a value this could not know.
/// </para>
/// <para>
/// Only this consumer's rows are touched. The table may be shared by several consumers, each with its
/// own retention and possibly in another process, and a sweep here knows nothing about theirs.
/// </para>
/// </remarks>
public sealed class InboxSweeper : BackgroundService, IPollReporter
{
    /// <summary>
    /// The longest a sweep waits for the next one, however long the retention is. A row therefore
    /// outlives its retention by at most one sweep, and a retention of days does not mean a single
    /// enormous delete every few days.
    /// </summary>
    private static readonly TimeSpan MaxSweepInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConsumerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InboxSweeper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxSweeper"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a scope per sweep.</param>
    /// <param name="options">The messaging options carrying the retention and the consumer identity.</param>
    /// <param name="timeProvider">Time provider used to work out what has expired.</param>
    /// <param name="logger">Logger used to report what was pruned.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="ConsumerOptions.InboxRetention"/> is set and not positive.
    /// </exception>
    /// <remarks>
    /// The retention is re-checked here although the messaging options validator already rejects it at
    /// startup: the pruner can be constructed without the container, and a retention of zero or less
    /// would have every sweep delete the records of the deliveries just completed.
    /// </remarks>
    public InboxSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<BsftMessagingOptions> options,
        TimeProvider timeProvider,
        ILogger<InboxSweeper> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value.Consumer;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.InboxRetention is { } retention && retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options), retention, "The inbox retention must be positive when it is set.");
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the prune loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal async Task RunLoopAsync(CancellationToken stoppingToken = default)
    {
        if (_options.InboxRetention is not { } retention)
        {
            // Registered unconditionally by AddBsftInboxEFCore, because the options are configured
            // after it and cannot be read at registration time. Leaving here is what "off by default"
            // means: no timer, no query, no row ever deleted.
            _logger.LogDebug(
                "The inbox is not purged because no retention is configured; records of completed deliveries are kept indefinitely.");

            return;
        }

        // Sweeping at the retention itself keeps a short retention honest without a second option to
        // configure, and the cap keeps a long one from becoming a rare, enormous delete.
        var interval = retention < MaxSweepInterval ? retention : MaxSweepInterval;

        await new PollLoop(ct => SweepAsync(retention, ct), this, interval)
            .RunAsync(stoppingToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One pass of the loop. Unlike the two outbox dispatchers the pruner runs on a fixed cadence: a
    /// sweep that deleted something does not mean more has expired since, so it always waits.
    /// </summary>
    private async Task<PollPassOutcome> SweepAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        await PurgeOnceAsync(retention, cancellationToken).ConfigureAwait(false);

        return PollPassOutcome.WaitForInterval;
    }

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure) =>
        _logger.LogError(failure, "The inbox purge sweep failed.");

    /// <inheritdoc />
    void IPollReporter.ReportPassCanceledWithoutStop() =>
        _logger.LogError("The inbox purge sweep was canceled although the sweeper was not asked to stop.");

    /// <inheritdoc />
    void IPollReporter.ReportStopping() => _logger.LogDebug("Inbox sweeper stopping.");

    /// <summary>Deletes this consumer's inbox records older than the retention.</summary>
    /// <param name="retention">How long a record is kept.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The number of records deleted.</returns>
    internal async Task<int> PurgeOnceAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var cutoff = (_timeProvider.GetUtcNow() - retention).UtcDateTime;
        var consumer = _options.ConsumerName;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IInboxDbContext>();

        // One set-based statement rather than loading the rows: nothing here reads a record, and
        // materialising a retention's worth of them only to delete them is the cost this exists to
        // avoid. The predicate is led by the consumer, so it is served by the prefix of the unique
        // index the table already carries; deliberately no index of its own on the age, because a sweep
        // runs at most hourly against a table this same sweep keeps small, and a second index would be
        // paid for on every delivery instead.
        var pruned = await db.InboxMessages
            .Where(e => e.Consumer == consumer && e.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pruned > 0)
            _logger.LogInformation(
                "Purged {PurgedCount} inbox records of consumer {ConsumerName} received before {Cutoff:o}. A redelivery of any of those events is handled again.",
                pruned,
                consumer,
                cutoff);

        return pruned;
    }
}
