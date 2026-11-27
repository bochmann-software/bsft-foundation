namespace Bsft.Retention.Watermarks;

/// <summary>
/// The disk watermark's policy: a crossed limit drains to the target, and the oldest items old enough
/// to go are chosen until they free what the target needs.
/// </summary>
/// <remarks>
/// A crossing is the archive holding more than <see cref="DiskWatermarkOptions.MaxArchiveBytes"/> or the
/// volume less than <see cref="DiskWatermarkOptions.MinAvailableBytes"/>, or a drain carried over from
/// an earlier sweep. Once crossed, both limits are held to their target: the archive to at most
/// <c>MaxArchiveBytes × TargetFillRatio</c>, the volume to at least <c>MinAvailableBytes ÷ TargetFillRatio</c>.
/// Deleting an item is taken to free its listed size on the volume.
/// </remarks>
internal sealed class DiskWatermarkPlanner : IDiskWatermarkPlanner
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public DiskWatermarkPlanResult Plan(DiskWatermarkPlanRequest request, DiskWatermarkOptions options)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var required = RequiredBytes(request, options);
        var evictions = new List<DiskWatermarkItemInfo>();
        long planned = 0;

        foreach (var item in request.Evictable)
        {
            if (planned >= required)
                break;

            evictions.Add(item);
            planned += item.Bytes;
        }

        var state = required == 0
            ? DiskWatermarkState.Within
            : planned >= required ? DiskWatermarkState.Draining : DiskWatermarkState.Unreachable;

        return new DiskWatermarkPlanResult(evictions, planned, required, state);
    }

    /// <summary>Works out how many bytes have to go for both limits to reach their target.</summary>
    /// <param name="request">The measurement.</param>
    /// <param name="options">The limits and the target fill ratio.</param>
    /// <returns>The bytes to free; zero when no limit is crossed and no drain is under way.</returns>
    private static long RequiredBytes(DiskWatermarkPlanRequest request, DiskWatermarkOptions options)
    {
        var archive = request.ArchiveBytes;
        var available = request.AvailableBytes;
        var maxArchive = options.MaxArchiveBytes;
        var minAvailable = available is null ? null : options.MinAvailableBytes;

        var crossed = request.IsDraining || archive > maxArchive || available < minAvailable;

        if (!crossed)
            return 0;

        var ratio = options.TargetFillRatio;

        var archiveExcess = maxArchive is { } max && archive is { } listed
            ? Math.Max(0, listed - Saturate(Math.Floor(max * ratio)))
            : 0;

        var availableShortfall = minAvailable is { } min && available is { } free
            ? Math.Max(0, Saturate(Math.Ceiling(min / ratio)) - free)
            : 0;

        return Math.Max(archiveExcess, availableShortfall);
    }

    /// <summary>Converts a byte count computed in floating point back to a whole number of bytes.</summary>
    /// <param name="bytes">The non-negative byte count.</param>
    /// <returns>The byte count, capped at <see cref="long.MaxValue"/>.</returns>
    /// <remarks>
    /// <c>(double)long.MaxValue</c> rounds up to 2^63, which does not convert back, so a limit near the
    /// top of the range is capped rather than allowed to overflow into a negative target.
    /// </remarks>
    private static long Saturate(double bytes) => bytes >= long.MaxValue ? long.MaxValue : (long)bytes;
}
