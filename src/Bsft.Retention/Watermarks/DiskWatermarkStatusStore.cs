using System.Collections.Concurrent;

namespace Bsft.Retention.Watermarks;

/// <summary>
/// The in-memory <see cref="IDiskWatermarkStatusStore"/>, registered once as a singleton for every
/// watermark pass of the host.
/// </summary>
/// <remarks>
/// Concurrent dictionaries because the sweeper of a pass writes while a reader such as a health probe
/// reads; each snapshot is an immutable record, so a reader sees one measurement whole or the one before
/// it. The drain flag is kept beside the snapshot rather than derived from it, because a failed
/// measurement replaces the snapshot but says nothing about whether a drain was under way.
/// </remarks>
internal sealed class DiskWatermarkStatusStore : IDiskWatermarkStatusStore
{
    private readonly ConcurrentDictionary<string, DiskWatermarkStatusInfo> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _draining = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public DiskWatermarkStatusInfo? Find(string passName)
    {
        if (passName is null)
            throw new ArgumentNullException(nameof(passName));

        return _snapshots.TryGetValue(passName, out var snapshot) ? snapshot : null;
    }

    /// <inheritdoc />
    public bool IsDraining(string passName)
    {
        if (passName is null)
            throw new ArgumentNullException(nameof(passName));

        return _draining.TryGetValue(passName, out var draining) && draining;
    }

    /// <inheritdoc />
    public void Save(string passName, DiskWatermarkStatusInfo snapshot)
    {
        if (passName is null)
            throw new ArgumentNullException(nameof(passName));
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        // Only the pass of this name writes, one sweep at a time, so the two entries need no lock
        // between them; readers only ever read the snapshot.
        if (snapshot.State != DiskWatermarkState.MeasurementFailed)
            _draining[passName] = snapshot.State is DiskWatermarkState.Draining or DiskWatermarkState.Unreachable;

        _snapshots[passName] = snapshot;
    }
}
