namespace Bsft.Retention.Watermarks;

/// <summary>
/// What an operator decides about one archive kept under a disk watermark: how large it may grow, how
/// much space its volume has to keep, and how far below either limit a crossing drains it.
/// </summary>
/// <remarks>
/// <para>
/// Registered as named options under <see cref="NameFor{TSource}"/>, the same name the pass's
/// <see cref="BsftRetentionOptions"/> use, and the configuration overload of
/// <c>AddBsftRetentionDiskWatermark</c> binds both from the one per-pass section under
/// <see cref="SectionName"/>.
/// </para>
/// <para>
/// Leaving both limits unset deletes nothing, the way an unset
/// <see cref="BsftRetentionOptions.RetentionPeriod"/> does: deleting data is a decision a host makes
/// explicitly.
/// </para>
/// </remarks>
public sealed class DiskWatermarkOptions
{
    /// <summary>
    /// The configuration section the per-pass sections sit under; the same one
    /// <see cref="BsftRetentionOptions.SectionName"/> names, since both options of a watermark pass
    /// are read from one section.
    /// </summary>
    public const string SectionName = BsftRetentionOptions.SectionName;

    /// <summary>
    /// Ten percent below a limit: enough room that the next few writes do not cross it again, little
    /// enough that a drain does not delete a tenth of the archive for one crossing.
    /// </summary>
    private const double DefaultTargetFillRatio = 0.9;

    /// <summary>
    /// The name the options of the watermark pass over <typeparamref name="TSource"/> are registered
    /// under, both these and its <see cref="BsftRetentionOptions"/>, and so its configuration section
    /// under <see cref="SectionName"/>.
    /// </summary>
    /// <typeparam name="TSource">The archive the watermark keeps.</typeparam>
    /// <returns>
    /// The pass name, <c>Bsft.Retention.Watermarks.DiskWatermarkRetentionPass&lt;</c> followed by the
    /// source type's full name and <c>&gt;</c>.
    /// </returns>
    public static string NameFor<TSource>()
        where TSource : class, IDiskWatermarkSource
        => BsftRetentionOptions.NameFor<DiskWatermarkRetentionPass<TSource>>();

    /// <summary>
    /// Gets or sets how many bytes the archive may hold before the oldest items are deleted. Defaults
    /// to <c>null</c>, which sets no limit on the archive itself.
    /// </summary>
    public long? MaxArchiveBytes { get; set; }

    /// <summary>
    /// Gets or sets how many bytes the volume under <see cref="VolumePath"/> has to keep available
    /// before the oldest items are deleted. Defaults to <c>null</c>, which sets no floor and measures
    /// no volume.
    /// </summary>
    public long? MinAvailableBytes { get; set; }

    /// <summary>
    /// Gets or sets a path on the volume <see cref="MinAvailableBytes"/> is measured on, typically the
    /// archive's own directory. Required when <see cref="MinAvailableBytes"/> is set.
    /// </summary>
    public string? VolumePath { get; set; }

    /// <summary>
    /// Gets or sets how full a crossing leaves the limits once it has drained, as a fraction in
    /// <c>(0, 1]</c>. Defaults to <c>0.9</c>.
    /// </summary>
    /// <remarks>
    /// A watermark crossed by one byte and drained back to exactly its limit is crossed again by the
    /// next write, so the archive would be trimmed by one item on every sweep. Once either limit is
    /// crossed, the pass instead deletes until the archive holds at most
    /// <see cref="MaxArchiveBytes"/> × this ratio and the volume keeps at least
    /// <see cref="MinAvailableBytes"/> ÷ this ratio, and it keeps draining towards that target over
    /// the following sweeps until it holds. One means no hysteresis.
    /// </remarks>
    public double TargetFillRatio { get; set; } = DefaultTargetFillRatio;
}
