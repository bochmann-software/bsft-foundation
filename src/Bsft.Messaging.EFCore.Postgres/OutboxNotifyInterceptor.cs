using Bsft.Messaging.EFCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Messaging.EFCore.Postgres;

/// <summary>
/// EF Core interceptor that announces a save which wrote outbox rows, so a listening dispatcher drains
/// the moment the transaction commits instead of waiting for its next poll.
/// </summary>
/// <remarks>
/// <para>
/// Add it to the context with <c>DbContextOptionsBuilder.AddInterceptors</c>, next to
/// <see cref="Bsft.Messaging.EFCore.Outbox.OutboxInterceptor"/> — that one stages the rows during
/// the save, this one announces them afterwards. The order the two are added in does not matter: this interceptor reads the rows off the
/// change tracker once the save has completed, not the buffer the other drains.
/// </para>
/// <para>
/// The announcement is made after the save rather than before it, which is what makes it safe. Inside
/// a transaction the notification is queued by the server and delivered on commit, so a rollback takes
/// it back with everything else; outside one the rows are already written by the time it goes out. The
/// reverse order — announcing first — would wake a listener that then finds nothing, and the rows it
/// missed would wait for a poll.
/// </para>
/// <para>
/// It rides <c>SavedChanges</c> for the synchronous save as well as the asynchronous one, because a
/// host that saves synchronously would otherwise get the polling latency it installed this package to
/// avoid, with nothing to tell it why.
/// </para>
/// </remarks>
public sealed class OutboxNotifyInterceptor : SaveChangesInterceptor
{
    private readonly IOutboxNotifier _notifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxNotifyInterceptor"/> class.
    /// </summary>
    /// <param name="notifier">What puts the announcement on the database.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notifier"/> is null.</exception>
    public OutboxNotifyInterceptor(IOutboxNotifier notifier) =>
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        // Context is always present for save interceptor callbacks.
        var context = eventData.Context!;

        if (HasOutboxRows(context))
            _notifier.Notify(context);

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context!;

        if (HasOutboxRows(context))
        {
            // Deliberately not the caller's token. The rows are committed by the time this runs, so a
            // request that was cancelled a moment ago has no say in whether the dispatcher is told
            // about them; taking its token would drop the wake-up for exactly the saves a shutting-down
            // host most wants delivered. The notification is one round trip and cannot block for long,
            // and it swallows its own failures, so nothing is left waiting on it either.
            await _notifier.NotifyAsync(context, CancellationToken.None).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decides whether this save is one the dispatcher should be told about, by asking the change
    /// tracker whether the context holds outbox rows at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The context tracks an <see cref="OutboxMessageEntity"/> only because the outbox interceptor
    /// added one, so this is an accurate reading of "this unit of work produced messages" and it is
    /// free of any assumption about the order the two interceptors were registered in. What it does
    /// cost is a repeated announcement when the same context is saved several times: the rows of the
    /// first save are still tracked during the second. That is one wake-up too many rather than one too
    /// few — the dispatcher answers it with a drain that finds nothing — and it is the trade this takes
    /// deliberately, because the opposite mistake loses a message's head start with nothing to show it.
    /// </para>
    /// <para>
    /// Change detection is switched off around the read, and that is not a micro-optimisation: with it
    /// on, <c>Entries&lt;T&gt;</c> runs a full <c>DetectChanges</c> over the tracker, and this runs after
    /// <em>every</em> completed save the application makes, outbox or not. The save pipeline detected
    /// changes on its way in and nothing has touched the graph since, so the second scan can only ever
    /// find what the first one did — it is work proportional to the tracked graph, paid per save, for an
    /// answer that is already known. The previous setting is restored so a context that had detection
    /// off, or on, is handed back as it was.
    /// </para>
    /// </remarks>
    private static bool HasOutboxRows(DbContext context)
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;

        tracker.AutoDetectChangesEnabled = false;

        try
        {
            return tracker.Entries<OutboxMessageEntity>().Any();
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }
}
