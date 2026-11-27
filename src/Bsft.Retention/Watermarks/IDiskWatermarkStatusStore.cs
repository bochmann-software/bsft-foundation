namespace Bsft.Retention.Watermarks;

/// <summary>
/// Keeps the last <see cref="DiskWatermarkStatusInfo"/> of every watermark pass of this instance, so a
/// drain carries over from one sweep to the next and the status can be read.
/// </summary>
/// <remarks>
/// In memory and per instance on purpose: the hysteresis it carries is a property of the sweeps this
/// instance runs, and a restart that forgets it costs at most one drain stopping between the target and
/// the limit until the limit is crossed again. Only the pass writes, so the write side stays internal
/// and <see cref="IDiskWatermarkStatusReader"/> is what the package publishes.
/// </remarks>
internal interface IDiskWatermarkStatusStore : IDiskWatermarkStatusReader
{
    /// <summary>Answers whether a drain of the pass has not reached its target yet.</summary>
    /// <param name="passName">The pass, as <see cref="DiskWatermarkOptions.NameFor{TSource}"/> names it.</param>
    /// <returns>
    /// True after a snapshot in <see cref="DiskWatermarkState.Draining"/> or
    /// <see cref="DiskWatermarkState.Unreachable"/>, false after one in <see cref="DiskWatermarkState.Within"/>
    /// or before any; a <see cref="DiskWatermarkState.MeasurementFailed"/> snapshot leaves it as it was.
    /// </returns>
    bool IsDraining(string passName);

    /// <summary>Stores the latest snapshot of a pass, replacing the one before.</summary>
    /// <param name="passName">The pass, as <see cref="DiskWatermarkOptions.NameFor{TSource}"/> names it.</param>
    /// <param name="snapshot">The snapshot to keep.</param>
    void Save(string passName, DiskWatermarkStatusInfo snapshot);
}
