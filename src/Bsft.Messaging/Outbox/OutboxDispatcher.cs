using Bsft.Core.Polling;
using Bsft.Core.Tenancy;
using Bsft.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Messaging.Outbox;

/// <summary>
/// Background service that drains the transactional outbox: it runs the shared drain pass over this
/// package's <see cref="IOutboxStore"/> and publishes every leased message through the
/// <see cref="IMessageBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at-least-once, and the order of the two steps is what buys it. The message was written
/// in the same transaction as the state change it describes, so it exists whenever that change exists;
/// it is marked processed only after it has been published, so a crash anywhere in between leaves it
/// pending and it is published again. What a crash can cost is a duplicate, never a lost event, which
/// is why <see cref="IIntegrationEventHandler{TEvent}"/> implementations must be idempotent.
/// </para>
/// <para>
/// Several application instances may run a dispatcher against the same outbox. A message is leased
/// before it is published, and only the lease holder can complete it, so the instances share the
/// backlog instead of each publishing all of it.
/// </para>
/// <para>
/// The pass itself is <c>OutboxDrainer&lt;IntegrationEventMessage&gt;</c>, which every outbox in the
/// host shares; the store, the publication and the retry schedule it runs on are composed where this
/// package is registered. What is left here is the loop: how long it waits when the outbox has run
/// dry, and what it says about a pass that failed.
/// </para>
/// </remarks>
public sealed class OutboxDispatcher : BackgroundService, IPollReporter, IOutboxProcessor
{
    private readonly IBackgroundTenantSource? _tenants;
    private readonly IOutboxDrainer<IntegrationEventMessage> _drainer;
    private readonly TimeSpan _pollInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxDispatcher"/> class.
    /// </summary>
    /// <param name="drainer">The drain pass over this package's outbox.</param>
    /// <param name="options">The messaging options carrying the outbox group.</param>
    /// <param name="timeProvider">The clock the wait between passes runs on.</param>
    /// <param name="logger">Logger used to report failed passes.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// The bounds of a pass are validated by the drainer rather than here, which is why this type does
    /// not check the interval it reads: a drainer this constructor could be handed is one whose
    /// options were already accepted.
    /// </remarks>
    public OutboxDispatcher(
        IOutboxDrainer<IntegrationEventMessage> drainer,
        IOptions<BsftMessagingOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _drainer = drainer ?? throw new ArgumentNullException(nameof(drainer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = options.Value.Outbox.PollInterval;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxDispatcher"/> class that runs each pass once per tenant
    /// of <paramref name="tenants"/>, with <see cref="BackgroundTenant.Current"/> set, so that every scope a pass opens
    /// resolves that tenant. The container picks this constructor when an <see cref="IBackgroundTenantSource"/> is
    /// registered.
    /// </summary>
    /// <param name="drainer">As for the constructor without the tenant source.</param>
    /// <param name="options">As for the constructor without the tenant source.</param>
    /// <param name="timeProvider">As for the constructor without the tenant source.</param>
    /// <param name="logger">As for the constructor without the tenant source.</param>
    /// <param name="tenants">The tenants the passes run for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public OutboxDispatcher(
        IOutboxDrainer<IntegrationEventMessage> drainer,
        IOptions<BsftMessagingOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcher> logger,
        IBackgroundTenantSource tenants)
        : this(drainer, options, timeProvider, logger)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunLoopAsync(stoppingToken);

    /// <summary>Runs the drain loop until the stopping token is cancelled.</summary>
    /// <param name="stoppingToken">The token that stops the loop when cancelled.</param>
    /// <returns>A task that completes when the loop has unwound.</returns>
    internal Task RunLoopAsync(CancellationToken stoppingToken = default) =>
        new PollLoop(DrainAsync, this, _pollInterval, _timeProvider).RunAsync(stoppingToken);

    /// <summary>
    /// One pass of the loop. A full batch means more work is waiting, so the dispatcher only backs
    /// off once the outbox has run dry.
    /// </summary>
    private async Task<PollPassOutcome> DrainAsync(CancellationToken cancellationToken) =>
        await ProcessOnceAsync(cancellationToken).ConfigureAwait(false) > 0
            ? PollPassOutcome.PollAgain
            : PollPassOutcome.WaitForInterval;

    /// <inheritdoc />
    void IPollReporter.ReportPassFailed(Exception failure) =>
        _logger.LogError(failure, "Outbox dispatch pass failed.");

    /// <inheritdoc />
    void IPollReporter.ReportPassCanceledWithoutStop() =>
        _logger.LogError("Outbox dispatch pass was canceled although the dispatcher was not asked to stop.");

    /// <inheritdoc />
    void IPollReporter.ReportStopping() => _logger.LogDebug("Outbox dispatcher stopping.");

    /// <inheritdoc />
    /// <remarks>
    /// With a tenant source the pass drains every tenant's outbox in turn, with
    /// <see cref="BackgroundTenant.Current"/> set, and returns the total. That holds for the poll and for a wake-up
    /// alike, so a notification listener calling this drains the tenants too.
    /// </remarks>
    public Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default) =>
        BackgroundTenantPasses.CountPerTenantAsync(_tenants, _drainer.DrainOnceAsync, cancellationToken);
}
