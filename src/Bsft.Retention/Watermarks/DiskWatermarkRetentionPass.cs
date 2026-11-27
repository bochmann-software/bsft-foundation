using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Retention.Watermarks;

/// <summary>
/// The retention pass that keeps an archive under a disk watermark: once the archive grows past its
/// limit or its volume runs short of space, it deletes the oldest items until both hold again with
/// room to spare.
/// </summary>
/// <typeparam name="TSource">The archive, which lists its items and deletes one.</typeparam>
/// <remarks>
/// <para>
/// An ordinary <see cref="IRetentionPass"/> driven by <see cref="RetentionSweeper{TPass}"/>, so the
/// loop, the pacing, the bound on one sweep, the stagger and the failure handling are the ones every
/// other pass has. The sweep's cutoff keeps its meaning: an item created at or after it is never
/// deleted, which makes <see cref="BsftRetentionOptions.RetentionPeriod"/> the minimum age an item
/// reaches before it may be evicted for space.
/// </para>
/// <para>
/// <see cref="BsftRetentionOptions.UseAbsoluteExpiry"/> is per pass and invalid for a watermark pass:
/// it would hand the pass the current time as its cutoff and evict even the newest items, so the host
/// fails at start when it is set. A host that needs both mixes them by registering separate pass
/// types, since the options are named per pass.
/// </para>
/// <para>
/// The sweeper resolves a pass from a fresh scope for every sweep and calls it batch after batch
/// against one cutoff, so the first call of a sweep measures and plans, and the following calls work
/// that plan off. The plan is measured once per sweep for the reason the cutoff is: a drain should
/// delete what it set out to delete, not chase a target that moves under it. What the plan is, is the
/// <see cref="IDiskWatermarkPlanner"/>'s decision; this class measures, records and deletes.
/// </para>
/// </remarks>
internal sealed class DiskWatermarkRetentionPass<TSource> : IRetentionPass
    where TSource : class, IDiskWatermarkSource
{
    private readonly TSource _source;
    private readonly IVolumeSpaceProvider _volumes;
    private readonly IDiskWatermarkPlanner _planner;
    private readonly DiskWatermarkOptions _options;
    private readonly IDiskWatermarkStatusStore _status;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DiskWatermarkRetentionPass<TSource>> _logger;
    private readonly string _passName;

    private IReadOnlyList<DiskWatermarkItemInfo>? _evictions;
    private int _next;
    private DiskWatermarkStatusInfo? _planned;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskWatermarkRetentionPass{TSource}"/> class.
    /// </summary>
    /// <param name="source">The archive kept under the watermark.</param>
    /// <param name="volumes">Measures the space available on the archive's volume.</param>
    /// <param name="planner">Decides what a measurement requires.</param>
    /// <param name="options">Supplies this pass's named watermark options.</param>
    /// <param name="status">Carries a drain from one sweep to the next and to the health check.</param>
    /// <param name="timeProvider">Stamps each measurement.</param>
    /// <param name="logger">Records what each measurement decided.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public DiskWatermarkRetentionPass(
        TSource source,
        IVolumeSpaceProvider volumes,
        IDiskWatermarkPlanner planner,
        IOptionsMonitor<DiskWatermarkOptions> options,
        IDiskWatermarkStatusStore status,
        TimeProvider timeProvider,
        ILogger<DiskWatermarkRetentionPass<TSource>> logger)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        _source = source ?? throw new ArgumentNullException(nameof(source));
        _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passName = DiskWatermarkOptions.NameFor<TSource>();
        _options = options.Get(_passName);
    }

    /// <inheritdoc />
    public async Task<int> PurgeBatchAsync(int batchSize, DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        _evictions ??= await PlanAsync(olderThan, cancellationToken).ConfigureAwait(false);

        var deleted = 0;

        while (deleted < batchSize && _next < _evictions.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _source.DeleteAsync(_evictions[_next], cancellationToken).ConfigureAwait(false);
            _next++;
            deleted++;
        }

        if (_next == _evictions.Count)
            CompletePlan();

        return deleted;
    }

    /// <summary>Measures the archive and its volume, and has the planner choose what this sweep deletes.</summary>
    /// <param name="olderThan">The sweep's cutoff; nothing created at or after it is chosen.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The items to delete, oldest first.</returns>
    /// <remarks>
    /// A measurement that fails is recorded as such before the failure goes on to the sweeper, so the
    /// health check reports a watermark it cannot see rather than the last one it could. The drain it
    /// interrupted is kept for the next sweep, and a host stopping mid-measurement records nothing.
    /// </remarks>
    private async Task<IReadOnlyList<DiskWatermarkItemInfo>> PlanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var measuredAt = _timeProvider.GetUtcNow();
        var measured = false;

        try
        {
            var request = await MeasureAsync(olderThan, cancellationToken).ConfigureAwait(false);
            measured = true;

            var plan = _planner.Plan(request, _options);

            _planned = new DiskWatermarkStatusInfo(plan.State, request.ArchiveBytes, request.AvailableBytes, plan.RequiredBytes, measuredAt);
            _status.Save(_passName, _planned);
            LogPlan(_planned, plan);

            return plan.Evictions;
        }
        finally
        {
            if (!measured && !cancellationToken.IsCancellationRequested)
                _status.Save(_passName, new DiskWatermarkStatusInfo(DiskWatermarkState.MeasurementFailed, null, null, 0, measuredAt));
        }
    }

    /// <summary>Measures the volume and, unless the volume alone decides, reads the whole archive once and sums it.</summary>
    /// <param name="olderThan">The sweep's cutoff; only items created before it are evictable.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The measurement, with the evictable items oldest first.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the source lists an item of negative size.</exception>
    /// <remarks>
    /// The listing is read to its end before anything is deleted, so a source backed by one
    /// <c>DbContext</c> never has a query open while it deletes. Every item counts towards the archive;
    /// an item listed out of order is still only evictable when it is old enough. Without an archive
    /// limit and without a drain under way, a volume that keeps its floor leaves nothing to decide, so
    /// the archive is not listed at all.
    /// </remarks>
    private async Task<DiskWatermarkPlanRequest> MeasureAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        long? available = _options.MinAvailableBytes is null || _options.VolumePath is null
            ? null
            : _volumes.GetAvailableBytes(_options.VolumePath);

        // A drain carries over: once a limit was crossed, the target rather than the limit decides
        // until it holds, which is the hysteresis that keeps the archive from flapping at its limit.
        var draining = _status.IsDraining(_passName);

        if (!draining && _options.MaxArchiveBytes is null && !(available < _options.MinAvailableBytes))
            return new DiskWatermarkPlanRequest(null, available, [], IsDraining: false);

        long archive = 0;
        var evictable = new List<DiskWatermarkItemInfo>();

        await foreach (var item in _source.ListOldestFirstAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item.Bytes < 0)
            {
                throw new InvalidOperationException(
                    $"The disk watermark source of {_passName} listed the item '{item.Key}' with a negative size of {item.Bytes} bytes.");
            }

            archive += item.Bytes;

            if (item.CreatedAt < olderThan)
                evictable.Add(item);
        }

        return new DiskWatermarkPlanRequest(archive, available, evictable, draining);
    }

    /// <summary>Records that every item this sweep chose is gone.</summary>
    /// <remarks>
    /// A drain that had a reachable target has reached it, so the next sweep starts from the limits
    /// again. An unreachable one stays recorded as such: it deleted what it could, and the next sweep
    /// keeps draining towards the target.
    /// </remarks>
    private void CompletePlan()
    {
        if (_planned is not { State: DiskWatermarkState.Draining } drained)
            return;

        _planned = drained with { State = DiskWatermarkState.Within };
        _status.Save(_passName, _planned);

        _logger.LogInformation(
            "The disk watermark of {RetentionPass} holds again after freeing at least {RequiredBytes} bytes.",
            _passName,
            drained.RequiredBytes);
    }

    /// <summary>Reports what a measurement decided.</summary>
    /// <param name="snapshot">The measurement and its outcome.</param>
    /// <param name="plan">The items chosen for deletion.</param>
    private void LogPlan(DiskWatermarkStatusInfo snapshot, DiskWatermarkPlanResult plan)
    {
        switch (snapshot.State)
        {
            case DiskWatermarkState.Draining:
                _logger.LogInformation(
                    "The disk watermark of {RetentionPass} is crossed with {ArchiveBytes} bytes archived and {AvailableBytes} bytes available; {PlannedCount} items holding {PlannedBytes} bytes are deleted to free {RequiredBytes} bytes.",
                    _passName,
                    snapshot.ArchiveBytes,
                    snapshot.AvailableBytes,
                    plan.Evictions.Count,
                    plan.PlannedBytes,
                    snapshot.RequiredBytes);
                break;

            case DiskWatermarkState.Unreachable:
                _logger.LogWarning(
                    "The disk watermark of {RetentionPass} cannot be reached with {ArchiveBytes} bytes archived and {AvailableBytes} bytes available: freeing {RequiredBytes} bytes needs more than the {PlannedBytes} bytes of the {PlannedCount} items old enough to delete, which are deleted all the same.",
                    _passName,
                    snapshot.ArchiveBytes,
                    snapshot.AvailableBytes,
                    snapshot.RequiredBytes,
                    plan.PlannedBytes,
                    plan.Evictions.Count);
                break;

            default:
                _logger.LogDebug(
                    "The disk watermark of {RetentionPass} holds with {ArchiveBytes} bytes archived and {AvailableBytes} bytes available.",
                    _passName,
                    snapshot.ArchiveBytes,
                    snapshot.AvailableBytes);
                break;
        }
    }
}
